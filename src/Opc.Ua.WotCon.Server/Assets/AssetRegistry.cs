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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Owns the per-asset state and the materialisation pipeline that
    /// turns a parsed <see cref="ThingDescription"/> into OPC UA nodes
    /// hanging off the WoT asset object.
    /// </summary>
    /// <remarks>
    /// Lifecycle methods are serialised through a <see cref="SemaphoreSlim"/>
    /// to avoid concurrent <c>CreateAsset</c>/<c>DeleteAsset</c> stomping on
    /// each other. Per-asset I/O (read/write/observe via the provider)
    /// runs outside the lock.
    /// </remarks>
    internal sealed class AssetRegistry : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AssetRegistry"/> class.
        /// </summary>
        /// <param name="manager">The node manager that owns this registry.</param>
        /// <param name="options">The WoT connectivity server options.</param>
        /// <param name="logger">The logger instance.</param>
        public AssetRegistry(
            WotConnectivityNodeManager manager,
            WotConnectivityServerOptions options,
            ILogger logger)
        {
            m_manager = manager;
            m_options = options;
            m_logger = logger;
        }

        /// <summary>
        /// Snapshot of currently registered asset names (test helper).
        /// </summary>
        public IReadOnlyCollection<string> AssetNames
        {
            get
            {
                lock (m_byName)
                {
                    return [.. m_byName.Keys];
                }
            }
        }

        /// <summary>Looks up an asset by NodeId. Returns <c>null</c> when missing.</summary>
        public AssetEntry? FindByNodeId(NodeId nodeId)
        {
            lock (m_byName)
            {
                return m_byNodeId.TryGetValue(nodeId, out AssetEntry? entry) ? entry : null;
            }
        }

        /// <summary>
        /// Looks up the asset entry that owns the specified property variable node.
        /// </summary>
        public bool TryGetProperty(
            NodeId variableId,
            out AssetEntry entry,
            out BaseDataVariableState variable,
            out WotPropertyTag tag)
        {
            lock (m_byName)
            {
                foreach (AssetEntry candidate in m_byNodeId.Values)
                {
                    if (candidate.Properties.TryGetValue(
                            variableId,
                            out (BaseDataVariableState Variable, WotPropertyTag Tag) hit))
                    {
                        entry = candidate;
                        variable = hit.Variable;
                        tag = hit.Tag;
                        return true;
                    }
                }
            }
            entry = null!;
            variable = null!;
            tag = null!;
            return false;
        }

        /// <summary>
        /// Looks up the asset entry that owns the specified action method node.
        /// </summary>
        public bool TryGetAction(
            NodeId methodId,
            out AssetEntry entry,
            out MethodState method,
            out WotActionTag tag)
        {
            lock (m_byName)
            {
                foreach (AssetEntry candidate in m_byNodeId.Values)
                {
                    if (candidate.Actions.TryGetValue(
                            methodId,
                            out (MethodState Method, WotActionTag Tag) hit))
                    {
                        entry = candidate;
                        method = hit.Method;
                        tag = hit.Tag;
                        return true;
                    }
                }
            }
            entry = null!;
            method = null!;
            tag = null!;
            return false;
        }

        /// <summary>
        /// Creates a new WoT asset object below the management object.
        /// Returns <see cref="StatusCodes.BadBrowseNameDuplicated"/> if
        /// <paramref name="assetName"/> is already in use.
        /// </summary>
        public async ValueTask<(ServiceResult Status, NodeId AssetId)> CreateAssetAsync(
            string assetName,
            CancellationToken ct)
        {
            ServiceResult nameCheck = WotAssetNameValidator.Validate(assetName);
            if (ServiceResult.IsBad(nameCheck))
            {
                return (nameCheck, NodeId.Null);
            }

            await m_writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                lock (m_byName)
                {
                    if (m_byName.ContainsKey(assetName))
                    {
                        return (
                            ServiceResult.Create(StatusCodes.BadBrowseNameDuplicated,
                                "An asset with this name already exists."),
                            NodeId.Null);
                    }
                }

                AssetEntry entry = await m_manager.CreateAssetNodeAsync(assetName, ct)
                    .ConfigureAwait(false);
                entry.FileManager = new WotAssetFileManager(
                    entry.Asset.WoTFile!,
                    m_options.MaxOpenFileHandlesPerAsset,
                    m_options.MaxThingDescriptionSize,
                    (td, token) => RebuildAsync(entry, td, persistOnSuccess: true, token),
                    m_logger,
                    m_manager.EnforceManagementAccess);

                lock (m_byName)
                {
                    m_byName[assetName] = entry;
                    m_byNodeId[entry.Asset.NodeId] = entry;
                }
                return (ServiceResult.Good, entry.Asset.NodeId);
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        /// <summary>
        /// Deletes the asset and removes all of its nodes.
        /// </summary>
        public async ValueTask<ServiceResult> DeleteAssetAsync(
            NodeId assetId,
            CancellationToken ct)
        {
            await m_writeLock.WaitAsync(ct).ConfigureAwait(false);
            AssetEntry? entry;
            try
            {
                lock (m_byName)
                {
                    if (!m_byNodeId.TryGetValue(assetId, out entry))
                    {
                        return ServiceResult.Create(StatusCodes.BadNotFound, "Asset not found.");
                    }
                    m_byName.Remove(entry.Name);
                    m_byNodeId.Remove(assetId);
                }

                if (entry.Provider != null)
                {
                    try
                    {
                        await entry.Provider.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.ProviderForAssetThrewOnDisposal(ex, entry.Name);
                    }
                }
                entry.FileManager?.Dispose();

                await m_manager.DeleteAssetNodeAsync(entry.Asset, ct).ConfigureAwait(false);
                DeleteTdFromDisk(entry.Name);
                await RemoveFromRegistryAsync(entry.Name, ct).ConfigureAwait(false);
                return ServiceResult.Good;
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        /// <summary>
        /// Creates an asset, synthesises a TD via the discovery provider,
        /// materialises it and persists the TD.
        /// </summary>
        /// <param name="assetName">The name for the new asset.</param>
        /// <param name="assetEndpoint">The endpoint URI to discover the TD from.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// A tuple containing the operation status and the created asset's node identifier.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The created asset could not be found after creation.
        /// </exception>
        public async ValueTask<(ServiceResult Status, NodeId AssetId)> CreateAssetForEndpointAsync(
            string assetName,
            string assetEndpoint,
            CancellationToken ct)
        {
            if (m_options.Discovery == null)
            {
                return (StatusCodes.BadNotSupported, NodeId.Null);
            }
            ServiceResult policyCheck = AssetEndpointValidator.Validate(
                assetEndpoint, m_options.AssetEndpointPolicy, out Uri? normalizedEndpoint);
            if (ServiceResult.IsBad(policyCheck))
            {
                m_logger.CreateAssetForEndpointRejected(policyCheck.StatusCode);
                return (policyCheck, NodeId.Null);
            }
            (ServiceResult createResult, NodeId assetId) = await CreateAssetAsync(assetName, ct)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(createResult))
            {
                return (createResult, assetId);
            }
            try
            {
                ThingDescription td = await RunWithPolicyTimeoutAsync(
                    inner => m_options.Discovery.CreateThingDescriptionAsync(
                        assetName, normalizedEndpoint!.AbsoluteUri, inner),
                    ct).ConfigureAwait(false);

                // §11 requires a Thing Description auto-generated from a
                // caller-chosen endpoint to be treated as untrusted input,
                // subject to the same Wot-Con 1.02 format validation an
                // uploaded document gets, and to materialize nothing when it
                // fails. The provider is pluggable and the endpoint it dialled
                // was chosen by the caller, so neither is a trusted source.
                if (!ThingDescriptionFormatValidator.HasIdentifyingMember(td))
                {
                    await DeleteAssetAsync(assetId, ct).ConfigureAwait(false);
                    m_logger.GeneratedThingDescriptionFailedFormatValidation(assetName);
                    return (ServiceResult.Create(StatusCodes.BadDecodingError,
                        "The Thing Description generated for this endpoint is not a Thing " +
                        "Description: it must carry a 'name' or 'title'."),
                        NodeId.Null);
                }

                AssetEntry entry = FindByNodeId(assetId)
                    ?? throw new InvalidOperationException("Asset disappeared after creation.");

                ServiceResult rebuild = await RebuildAsync(entry, td, persistOnSuccess: true, ct)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(rebuild))
                {
                    await DeleteAssetAsync(assetId, ct).ConfigureAwait(false);
                    return (rebuild, NodeId.Null);
                }
                return (ServiceResult.Good, assetId);
            }
            catch (NotSupportedException ex)
            {
                await DeleteAssetAsync(assetId, ct).ConfigureAwait(false);
                m_logger.CreateAssetForEndpointProviderRejected(ex, assetName);
                return (ToClientStatus(ex, StatusCodes.BadNotSupported, "CreateAssetForEndpoint"), NodeId.Null);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // Policy timeout fired; caller's token is still alive.
                await DeleteAssetAsync(assetId, ct).ConfigureAwait(false);
                m_logger.CreateAssetForEndpointTimedOut(
                    ex,
                    m_options.AssetEndpointPolicy.MaxOperationTimeout,
                    assetName);
                return (ServiceResult.Create(StatusCodes.BadTimeout,
                    "Discovery provider exceeded AssetEndpointPolicy.MaxOperationTimeout."),
                    NodeId.Null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await DeleteAssetAsync(assetId, ct).ConfigureAwait(false);
                m_logger.CreateAssetForEndpointFailed(ex, assetName);
                return (ToClientStatus(ex, MapToStatusCode(ex), "CreateAssetForEndpoint"), NodeId.Null);
            }
        }

        /// <summary>
        /// Discovers available asset endpoints via the configured discovery provider.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// A tuple containing the operation status and the list of discovered endpoints.
        /// </returns>
        public async ValueTask<(ServiceResult Status, IReadOnlyList<string> Endpoints)> DiscoverAssetsAsync(
            CancellationToken ct)
        {
            if (m_options.Discovery == null)
            {
                return (StatusCodes.BadNotSupported, Array.Empty<string>());
            }
            try
            {
                IReadOnlyList<string> endpoints = await m_options.Discovery.DiscoverAsync(ct)
                    .ConfigureAwait(false);
                return (ServiceResult.Good, FilterDiscoveredEndpoints(endpoints));
            }
            catch (NotSupportedException ex)
            {
                m_logger.DiscoverAssetsNotSupported(ex);
                return (ToClientStatus(ex, StatusCodes.BadNotSupported, "DiscoverAssets"),
                    Array.Empty<string>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.DiscoverAssetsFailed(ex);
                return (ToClientStatus(ex, MapToStatusCode(ex), "DiscoverAssets"),
                    Array.Empty<string>());
            }
        }

        /// <summary>
        /// Tests connectivity to the specified asset endpoint.
        /// </summary>
        /// <param name="assetEndpoint">The endpoint URI to test.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// A tuple containing the operation status, a success flag and a status text.
        /// </returns>
        public async ValueTask<(ServiceResult Status, bool Success, string StatusText)> ConnectionTestAsync(
            string assetEndpoint,
            CancellationToken ct)
        {
            if (m_options.Discovery == null)
            {
                return (StatusCodes.BadNotSupported, false, string.Empty);
            }
            ServiceResult policyCheck = AssetEndpointValidator.Validate(
                assetEndpoint, m_options.AssetEndpointPolicy, out Uri? normalizedEndpoint);
            if (ServiceResult.IsBad(policyCheck))
            {
                m_logger.ConnectionTestRejected(policyCheck.StatusCode);
                return (policyCheck, false, string.Empty);
            }
            try
            {
                (bool success, string status) = await RunWithPolicyTimeoutAsync(
                    inner => m_options.Discovery.TestAsync(
                        normalizedEndpoint!.AbsoluteUri, inner),
                    ct).ConfigureAwait(false);
                return (ServiceResult.Good, success, status ?? string.Empty);
            }
            catch (NotSupportedException ex)
            {
                m_logger.ConnectionTestNotSupported(ex);
                return (ToClientStatus(ex, StatusCodes.BadNotSupported, "ConnectionTest"),
                    false, string.Empty);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.ConnectionTestFailed(ex);
                return (ToClientStatus(ex, MapToStatusCode(ex), "ConnectionTest"),
                    false, string.Empty);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                m_logger.ConnectionTestTimedOut(ex, m_options.AssetEndpointPolicy.MaxOperationTimeout);
                return (ServiceResult.Create(StatusCodes.BadTimeout,
                    "Discovery provider exceeded AssetEndpointPolicy.MaxOperationTimeout."),
                    false, string.Empty);
            }
        }

        /// <summary>
        /// Runs <paramref name="work"/> under a linked cancellation
        /// source that fires after
        /// <see cref="AssetEndpointPolicy.MaxOperationTimeout"/> on top
        /// of the caller's <paramref name="ct"/>. A
        /// <see cref="TimeSpan.Zero"/> timeout disables the
        /// per-operation bound. When the timeout fires the inner
        /// task's <see cref="OperationCanceledException"/> is rethrown
        /// — the caller's catch filter
        /// (<c>when (!ct.IsCancellationRequested)</c>) distinguishes
        /// policy-timeout from caller-cancellation.
        /// </summary>
        /// <typeparam name="T">Return type of the inner work.</typeparam>
        private async ValueTask<T> RunWithPolicyTimeoutAsync<T>(
            Func<CancellationToken, ValueTask<T>> work,
            CancellationToken ct)
        {
            TimeSpan timeout = m_options.AssetEndpointPolicy.MaxOperationTimeout;
            if (timeout <= TimeSpan.Zero)
            {
                return await work(ct).ConfigureAwait(false);
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            return await work(linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Rebuilds the variable + method children of an asset from a TD,
        /// reconnecting (or replacing) its provider as appropriate.
        /// </summary>
        /// <remarks>
        /// TD property / action keys flow through
        /// <see cref="WotChildNameValidator.Validate"/> before they
        /// become <see cref="NodeId"/> path segments or
        /// <see cref="QualifiedName"/> browse names. Names that fail
        /// validation (or duplicate an earlier valid name) are skipped
        /// with a per-child warning; the remaining valid children
        /// still materialise so a single bad TD entry does not poison
        /// the whole asset.
        /// </remarks>
        /// <param name="entry">The asset entry to rebuild.</param>
        /// <param name="td">The thing description to materialise.</param>
        /// <param name="persistOnSuccess">
        /// Whether to persist the TD to disk on success.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A <see cref="ServiceResult"/> indicating the outcome.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="entry"/> is null.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="td"/> is null.
        /// </exception>
        public async ValueTask<ServiceResult> RebuildAsync(
            AssetEntry entry,
            ThingDescription td,
            bool persistOnSuccess,
            CancellationToken ct)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            if (td is null)
            {
                throw new ArgumentNullException(nameof(td));
            }
            IWotAssetProviderFactory? factory = null;
            foreach (IWotAssetProviderFactory candidate in m_options.Bindings)
            {
                if (candidate.CanHandle(td))
                {
                    factory = candidate;
                    break;
                }
            }
            if (factory == null)
            {
                return ServiceResult.Create(StatusCodes.BadNotSupported,
                    "No registered WoT binding factory accepts this Thing Description.");
            }

            IWotAssetProvider provider;
            try
            {
                provider = await factory.ConnectAsync(td, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.BindingFactoryFailedToConnectAsset(ex, factory.GetType().Name, entry.Name);
                return ToClientStatus(ex, MapToStatusCode(ex), "Asset rebuild");
            }

            await m_writeLock.WaitAsync(ct).ConfigureAwait(false);
            int skippedAffordances = 0;
            try
            {
                if (entry.Provider != null)
                {
                    // Retire the outgoing generation before the provider goes
                    // away so an occurrence still in flight is dropped rather
                    // than reported against an event type about to be removed.
                    entry.EventGeneration = new object();

                    try
                    {
                        await UnsubscribeEventsAsync(entry, entry.Provider, ct).ConfigureAwait(false);
                        await entry.Provider.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.PreviousProviderThrewOnDisposal(ex, entry.Name);
                    }
                }
                entry.Provider = provider;

                ClearDynamicChildren(entry);

                // An affordance the TD declares but this Server cannot
                // materialise is skipped so the rest of the asset stays usable.
                // Skipping is not the same as succeeding, though: reporting a
                // plain Good would tell an operator the whole TD was applied
                // while an alarm they authored had silently vanished.
                if (td.Properties != null)
                {
                    var seen = new HashSet<string>(
                        StringComparer.Ordinal);
                    foreach (KeyValuePair<string, WotProperty> kv
                        in td.Properties)
                    {
                        if (!TryValidateChildName(entry.Name, "property", kv.Key))
                        {
                            skippedAffordances++;
                            continue;
                        }
                        if (!seen.Add(kv.Key))
                        {
                            m_logger.SkippingDuplicateTdProperty(
                                WotChildNameValidator.SanitiseForLog(kv.Key),
                                entry.Name);
                            skippedAffordances++;
                            continue;
                        }
                        BuildPropertyNode(entry, kv.Key, kv.Value);
                    }
                }
                if (td.Actions != null)
                {
                    var seen = new HashSet<string>(
                        StringComparer.Ordinal);
                    foreach (KeyValuePair<string, WotAction> kv
                        in td.Actions)
                    {
                        if (!TryValidateChildName(entry.Name, "action", kv.Key))
                        {
                            skippedAffordances++;
                            continue;
                        }
                        if (!seen.Add(kv.Key))
                        {
                            m_logger.SkippingDuplicateTdAction(
                                WotChildNameValidator.SanitiseForLog(kv.Key),
                                entry.Name);
                            skippedAffordances++;
                            continue;
                        }
                        BuildActionNode(entry, kv.Key, kv.Value);
                    }
                }
                if (td.Events != null)
                {
                    var seen = new HashSet<string>(
                        StringComparer.Ordinal);
                    foreach (KeyValuePair<string, WotEvent> kv
                        in td.Events)
                    {
                        if (!TryValidateChildName(entry.Name, "event", kv.Key))
                        {
                            skippedAffordances++;
                            continue;
                        }
                        if (!seen.Add(kv.Key))
                        {
                            m_logger.SkippingDuplicateTdEvent(
                                WotChildNameValidator.SanitiseForLog(kv.Key),
                                entry.Name);
                            skippedAffordances++;
                            continue;
                        }
                        if (!BuildEventNode(entry, kv.Key, kv.Value))
                        {
                            skippedAffordances++;
                        }
                    }
                }

                if (entry.Events.Count > 0)
                {
                    // The asset raises every occurrence for the lifetime of
                    // the TD generation; the server's subscription machinery
                    // decides which clients receive it, so there is no
                    // per-monitored-item subscribe/unsubscribe here.
                    await m_manager.EnableAssetEventsAsync(entry.Asset, ct).ConfigureAwait(false);
                    await SubscribeEventsAsync(entry, ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(td.Base))
                {
                    entry.Asset.AddAssetEndpoint(m_manager.SystemContext);
                    entry.Asset.AssetEndpoint!.Value = td.Base!;
                }

                entry.Asset.ClearChangeMasks(m_manager.SystemContext, includeChildren: true);

                if (persistOnSuccess)
                {
                    PersistTdToDisk(entry.Name, td);
                }

                await MirrorToRegistryAsync(entry.Name, td, ct).ConfigureAwait(false);
            }
            finally
            {
                m_writeLock.Release();
            }

            // Still Good - the asset is usable and IsGood callers are
            // unaffected - but an operator reading the code learns the TD was
            // not applied in full rather than being told everything worked.
            return skippedAffordances == 0
                ? ServiceResult.Good
                : ServiceResult.Create(
                    StatusCodes.GoodResultsMayBeIncomplete,
                    "{0} affordance(s) of the Thing Description were skipped; see the log " +
                    "for the reason for each.",
                    skippedAffordances);
        }

        private void ClearDynamicChildren(AssetEntry entry)
        {
            foreach (KeyValuePair<NodeId, (BaseDataVariableState Variable, WotPropertyTag _)> kv in entry.Properties)
            {
                entry.Asset.RemoveChild(kv.Value.Variable);
            }
            entry.Properties.Clear();

            foreach (KeyValuePair<NodeId, (MethodState Method, WotActionTag _)> kv in entry.Actions)
            {
                entry.Asset.RemoveChild(kv.Value.Method);
            }
            entry.Actions.Clear();

            // Event types are not children of the asset object; they are
            // owned by the node manager, so only the tag map is cleared here
            // and the type nodes are dropped by RemoveEventTypes.
            RemoveEventTypes(entry);
            entry.Events.Clear();
        }

        /// <summary>
        /// Runs the TD child name through <see cref="WotChildNameValidator"/>
        /// before it is used to mint a NodeId / QualifiedName. On
        /// rejection a single warning is logged (with the name
        /// sanitised via <see cref="WotChildNameValidator.SanitiseForLog"/>
        /// so a hostile name can't reshape the log line) and the
        /// caller skips the child.
        /// </summary>
        private bool TryValidateChildName(string assetName, string kind, string? childName)
        {
            ServiceResult result = WotChildNameValidator.Validate(childName);
            if (ServiceResult.IsGood(result))
            {
                return true;
            }
            LocalizedText reason = result.LocalizedText;
            m_logger.SkippingTd(
                kind,
                WotChildNameValidator.SanitiseForLog(childName),
                assetName,
                reason.IsNull ? "name validation failed" : reason.Text);
            return false;
        }

        private void BuildPropertyNode(AssetEntry entry, string name, WotProperty property)
        {
            bool mapped = WotPropertyMapper.TryMap(property, out NodeId dataType, out int valueRank);
            ushort ns = m_manager.AssetNamespaceIndex;
            NodeId nodeId = m_manager.AllocateChildNodeId(entry.Name, "props", name);
            var hasWotComponent = ExpandedNodeId.ToNodeId(
                ReferenceTypeIds.HasWoTComponent,
                m_manager.Server.NamespaceUris);

            var variable = new BaseDataVariableState(entry.Asset)
            {
                SymbolicName = name,
                NodeId = nodeId,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(property.Title ?? name),
                Description = property.Description != null ? new LocalizedText(property.Description) : LocalizedText.Null,
                DataType = mapped ? dataType : Ua.DataTypeIds.BaseDataType,
                ValueRank = mapped ? valueRank : ValueRanks.Scalar,
                AccessLevel = property.ReadOnly ? AccessLevels.CurrentRead : AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = property.ReadOnly ? AccessLevels.CurrentRead : AccessLevels.CurrentReadOrWrite,
                Historizing = false,
                ReferenceTypeId = hasWotComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType
            };
            variable.AddReference(hasWotComponent, isInverse: true, entry.Asset.NodeId);
            entry.Asset.AddReference(hasWotComponent, isInverse: false, variable.NodeId);
            entry.Asset.AddChild(variable);

            JsonElement? form = property.Forms?.Count > 0 ? property.Forms[0] : null;
            var tag = new WotPropertyTag(
                name,
                nodeId,
                variable.DataType,
                variable.ValueRank,
                property.ReadOnly,
                property.Observable,
                form);

            if (!mapped)
            {
                variable.Value = Variant.Null;
                variable.StatusCode = StatusCodes.BadConfigurationError;
                variable.OnSimpleReadValueAsync = static (_, _, _) =>
                    new ValueTask<AttributeSimpleReadResult>(
                        new AttributeSimpleReadResult(StatusCodes.BadConfigurationError, Variant.Null));
            }
            else
            {
                variable.Value = TypeInfo.GetDefaultVariantValue(variable.DataType, variable.ValueRank);
                variable.OnSimpleReadValueAsync = (_, _, ct) =>
                    ReadFromProviderAsync(entry, tag, ct);
                if (!property.ReadOnly)
                {
                    variable.OnSimpleWriteValueAsync = (_, _, value, ct) =>
                        WriteToProviderAsync(entry, tag, value, ct);
                }
            }

            entry.Properties[nodeId] = (variable, tag);
        }

        private void BuildActionNode(AssetEntry entry, string name, WotAction action)
        {
            ushort ns = m_manager.AssetNamespaceIndex;
            NodeId nodeId = m_manager.AllocateChildNodeId(entry.Name, "actions", name);

            var method = new MethodState(entry.Asset)
            {
                SymbolicName = name,
                NodeId = nodeId,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(action.Title ?? name),
                Description = action.Description != null ? new LocalizedText(action.Description) : LocalizedText.Null,
                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                Executable = true,
                UserExecutable = true
            };
            NodeId conditionMethodId = ResolveConditionMethod(action.ConditionAction);
            if (!conditionMethodId.IsNull)
            {
                method.MethodDeclarationId = conditionMethodId;
            }
            method.AddReference(Ua.ReferenceTypeIds.HasComponent, isInverse: true, entry.Asset.NodeId);
            entry.Asset.AddReference(Ua.ReferenceTypeIds.HasComponent, isInverse: false, method.NodeId);
            entry.Asset.AddChild(method);

            IReadOnlyList<Argument> inputArgs = WotActionMapper.BuildArguments(action.Input);
            IReadOnlyList<Argument> outputArgs = WotActionMapper.BuildArguments(action.Output);

            if (inputArgs.Count > 0)
            {
                var argsArray = new Argument[inputArgs.Count];
                for (int i = 0; i < inputArgs.Count; i++)
                {
                    argsArray[i] = inputArgs[i];
                }
                var inputProperty =
                    PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
                inputProperty.NodeId = m_manager.AllocateChildNodeId(entry.Name, "actions", name + "_in");
                inputProperty.BrowseName = new QualifiedName(Ua.BrowseNames.InputArguments);
                inputProperty.DisplayName = new LocalizedText(Ua.BrowseNames.InputArguments);
                inputProperty.DataType = Ua.DataTypeIds.Argument;
                inputProperty.ValueRank = ValueRanks.OneDimension;
                inputProperty.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
                inputProperty.TypeDefinitionId = VariableTypeIds.PropertyType;
                inputProperty.Value = new ArrayOf<Argument>(argsArray);
                method.InputArguments = inputProperty;
                method.AddChild(inputProperty);
            }
            if (outputArgs.Count > 0)
            {
                var argsArray = new Argument[outputArgs.Count];
                for (int i = 0; i < outputArgs.Count; i++)
                {
                    argsArray[i] = outputArgs[i];
                }
                var outputProperty =
                    PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
                outputProperty.NodeId = m_manager.AllocateChildNodeId(entry.Name, "actions", name + "_out");
                outputProperty.BrowseName = new QualifiedName(Ua.BrowseNames.OutputArguments);
                outputProperty.DisplayName = new LocalizedText(Ua.BrowseNames.OutputArguments);
                outputProperty.DataType = Ua.DataTypeIds.Argument;
                outputProperty.ValueRank = ValueRanks.OneDimension;
                outputProperty.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
                outputProperty.TypeDefinitionId = VariableTypeIds.PropertyType;
                outputProperty.Value = new ArrayOf<Argument>(argsArray);
                method.OutputArguments = outputProperty;
                method.AddChild(outputProperty);
            }

            JsonElement? form = action.Forms?.Count > 0 ? action.Forms[0] : null;
            var tag = new WotActionTag(
                name,
                nodeId,
                inputArgs,
                outputArgs,
                form,
                action.ConditionAction,
                action.ActsOn);

            method.OnCallMethod2Async = (
                _,
                _,
                _,
                inputArguments,
                outputArguments,
                ct) =>
                InvokeActionAsync(entry, tag, inputArguments, outputArguments, ct);

            entry.Actions[nodeId] = (method, tag);
        }

        private static NodeId ResolveConditionMethod(string? conditionAction)
        {
            return conditionAction switch
            {
                "Acknowledge" => Ua.MethodIds.AcknowledgeableConditionType_Acknowledge,
                "Confirm" => Ua.MethodIds.AcknowledgeableConditionType_Confirm,
                "AddComment" => Ua.MethodIds.ConditionType_AddComment,
                "Enable" => Ua.MethodIds.ConditionType_Enable,
                "Disable" => Ua.MethodIds.ConditionType_Disable,
                _ => NodeId.Null
            };
        }

        /// <summary>
        /// Materialises a TD event affordance as an OPC UA EventType
        /// (OPC 10100-1 §6.3.10) whose fields come from the event's
        /// <c>data</c> schema, and makes the owning asset a notifier for it.
        /// </summary>
        /// <returns>
        /// <c>false</c> when the affordance was skipped, so the caller can
        /// report that the Thing Description was not applied in full.
        /// </returns>
        private bool BuildEventNode(AssetEntry entry, string name, WotEvent evt)
        {
            ushort ns = m_manager.AssetNamespaceIndex;
            NodeId eventTypeId = m_manager.AllocateChildNodeId(entry.Name, "events", name);
            NodeId superTypeId = ResolveEventSuperType(evt);
            if (superTypeId.IsNull)
            {
                m_logger.SkippingTd(
                    "event",
                    WotChildNameValidator.SanitiseForLog(name),
                    entry.Name,
                    "uav:conditionType could not be resolved to an OPC UA ConditionType");
                return false;
            }
            if (IsKnownConditionType(superTypeId))
            {
                EnsureConditionTypeHierarchy();
            }

            var eventType = new BaseObjectTypeState
            {
                SymbolicName = name,
                NodeId = eventTypeId,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(evt.Title ?? name),
                Description = evt.Description != null
                    ? new LocalizedText(evt.Description)
                    : LocalizedText.Null,
                SuperTypeId = superTypeId,
                IsAbstract = false
            };
            eventType.AddReference(
                Ua.ReferenceTypeIds.HasSubtype, isInverse: true, superTypeId);

            IReadOnlyList<Argument> fields = WotActionMapper.BuildArguments(evt.Data);
            foreach (Argument field in fields)
            {
                var property = new PropertyState(eventType)
                {
                    NodeId = m_manager.AllocateChildNodeId(
                        entry.Name, "events", $"{name}_{field.Name}"),
                    BrowseName = new QualifiedName(field.Name, ns),
                    DisplayName = new LocalizedText(field.Name),
                    Description = field.Description,
                    DataType = field.DataType,
                    ValueRank = field.ValueRank,
                    ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    ModellingRuleId = Ua.ObjectIds.ModellingRule_Mandatory
                };
                eventType.AddChild(property);
            }

            // The asset object notifies the event, so a client subscribing to
            // the asset (or to the Server object) receives it.
            entry.Asset.AddReference(
                Ua.ReferenceTypeIds.GeneratesEvent, isInverse: false, eventTypeId);

            m_manager.AddEventTypeNode(eventType);

            JsonElement? form = evt.Forms?.Count > 0 ? evt.Forms[0] : null;
            var tag = new WotEventTag(
                name,
                eventTypeId,
                entry.Asset.NodeId,
                fields,
                (ushort)EventSeverity.Medium,
                form);

            entry.Events[eventTypeId] = (eventType, tag);
            return true;
        }

        /// <summary>
        /// Resolves the EventType supertype for a WoT event affordance.
        /// </summary>
        private NodeId ResolveEventSuperType(WotEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(evt.ConditionTypeId))
            {
                return ParseNodeId(evt.ConditionTypeId);
            }

            if (string.IsNullOrWhiteSpace(evt.ConditionType))
            {
                return Ua.ObjectTypeIds.BaseEventType;
            }

            return ResolveKnownConditionType(evt.ConditionType);
        }

        private void EnsureConditionTypeHierarchy()
        {
            if (m_manager.Server.TypeTree is not TypeTable typeTree)
            {
                return;
            }

            typeTree.AddSubtype(Ua.ObjectTypeIds.ConditionType, Ua.ObjectTypeIds.BaseEventType);
            typeTree.AddSubtype(Ua.ObjectTypeIds.AcknowledgeableConditionType, Ua.ObjectTypeIds.ConditionType);
            typeTree.AddSubtype(Ua.ObjectTypeIds.AlarmConditionType, Ua.ObjectTypeIds.AcknowledgeableConditionType);
            typeTree.AddSubtype(Ua.ObjectTypeIds.LimitAlarmType, Ua.ObjectTypeIds.AlarmConditionType);
        }

        private static bool IsKnownConditionType(NodeId nodeId)
        {
            return nodeId == Ua.ObjectTypeIds.ConditionType ||
                nodeId == Ua.ObjectTypeIds.AcknowledgeableConditionType ||
                nodeId == Ua.ObjectTypeIds.AlarmConditionType ||
                nodeId == Ua.ObjectTypeIds.LimitAlarmType;
        }

        private NodeId ResolveKnownConditionType(string conditionType)
        {
            return LocalName(conditionType) switch
            {
                "ConditionType" => Ua.ObjectTypeIds.ConditionType,
                "AcknowledgeableConditionType" => Ua.ObjectTypeIds.AcknowledgeableConditionType,
                "AlarmConditionType" => Ua.ObjectTypeIds.AlarmConditionType,
                "LimitAlarmType" => Ua.ObjectTypeIds.LimitAlarmType,
                _ => NodeId.Null
            };
        }

        private NodeId ParseNodeId(string text)
        {
            try
            {
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(
                        ExpandedNodeId.Parse(text),
                        m_manager.Server.NamespaceUris);
                }
                return NodeId.Parse(text);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return NodeId.Null;
            }
        }

        private static string LocalName(string value)
        {
            int separator = Math.Max(
                value.LastIndexOf(':'),
                Math.Max(value.LastIndexOf('#'), value.LastIndexOf('/')));
            return separator >= 0 && separator + 1 < value.Length
                ? value[(separator + 1)..]
                : value;
        }

        /// <summary>
        /// Chooses the severity to publish for one occurrence: the value the
        /// provider supplied when it is in the OPC 10000-5 range, otherwise
        /// the server's fallback.
        /// </summary>
        /// <remarks>
        /// The fallback is implementation configuration, not Thing Description
        /// metadata. A provider that supplies nothing, or an out-of-range
        /// value, gets that fallback rather than an invalid Severity on the
        /// wire.
        /// </remarks>
        private static ushort EffectiveSeverity(ushort? severity, WotEventTag tag)
        {
            return severity is not null && severity.Value is >= 1 and <= 1000
                ? severity.Value
                : tag.Severity;
        }

        /// <summary>
        /// Drops the EventTypes materialised for an asset's previous TD
        /// generation, including the asset's <c>GeneratesEvent</c> references
        /// to them, so a re-applied TD does not accumulate stale event types.
        /// </summary>
        private void RemoveEventTypes(AssetEntry entry)
        {
            foreach (KeyValuePair<NodeId, (BaseObjectTypeState _, WotEventTag Tag)> kv in entry.Events)
            {
                entry.Asset.RemoveReference(
                    Ua.ReferenceTypeIds.GeneratesEvent, isInverse: false, kv.Key);
                m_manager.RemoveEventTypeNode(kv.Key);
            }
        }

        /// <summary>
        /// Subscribes the asset's provider to every materialised event
        /// affordance. A provider that fails one subscription is logged and
        /// the remaining affordances are still attempted, so one unsupported
        /// event does not silence the rest.
        /// </summary>
        private async ValueTask SubscribeEventsAsync(AssetEntry entry, CancellationToken ct)
        {
            IWotAssetProvider? provider = entry.Provider;
            if (provider == null)
            {
                return;
            }

            foreach (KeyValuePair<NodeId, (BaseObjectTypeState _, WotEventTag Tag)> kv in entry.Events)
            {
                WotEventTag tag = kv.Value.Tag;
                object generation = entry.EventGeneration;
                void OnEvent(
                    WotEventTag t,
                    IReadOnlyList<Variant> fields,
                    LocalizedText? message,
                    ushort? severity,
                    DateTime timestamp)
                {
                    ReportWotEvent(entry, generation, t, fields, message, severity, timestamp);
                }

                try
                {
                    await provider
                        .SubscribeEventAsync(tag, EventSubscriberId, OnEvent, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    m_logger.EventSubscribeFailed(ex, entry.Name, tag.Name);
                }
            }
        }

        /// <summary>
        /// Stops the provider's event subscriptions for the outgoing Thing
        /// Description generation. A provider that outlives the generation
        /// (for example a pooled or shared connection) would otherwise keep
        /// pushing occurrences for event types that no longer exist.
        /// </summary>
        private async ValueTask UnsubscribeEventsAsync(
            AssetEntry entry,
            IWotAssetProvider provider,
            CancellationToken ct)
        {
            foreach (KeyValuePair<NodeId, (BaseObjectTypeState _, WotEventTag Tag)> kv in entry.Events)
            {
                try
                {
                    await provider
                        .UnsubscribeEventAsync(kv.Value.Tag, EventSubscriberId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    m_logger.EventUnsubscribeFailed(ex, entry.Name, kv.Value.Tag.Name);
                }
            }
        }

        /// <summary>
        /// Reports a provider-raised WoT event occurrence on the asset that
        /// notifies it.
        /// </summary>
        private void ReportWotEvent(
            AssetEntry entry,
            object generation,
            WotEventTag tag,
            IReadOnlyList<Variant> fields,
            LocalizedText? message,
            ushort? severity,
            DateTime timestamp)
        {
            if (!ReferenceEquals(generation, entry.EventGeneration))
            {
                // The Thing Description this subscription belongs to has been
                // replaced; the event type it names no longer exists.
                return;
            }

            ISystemContext context = m_manager.SystemContext;

            var e = new BaseEventState(null);
            e.Initialize(
                context,
                source: entry.Asset,
                EventSeverity.Medium,
                message ?? new LocalizedText(tag.Name));

            e.SetChildValue(context, Ua.BrowseNames.EventType, tag.EventTypeId, false);
            e.SetChildValue(context, Ua.BrowseNames.SourceNode, entry.Asset.NodeId, false);
            e.SetChildValue(context, Ua.BrowseNames.SourceName, entry.Name, false);
            e.SetChildValue(context, Ua.BrowseNames.Time, new DateTimeUtc(timestamp), false);
            e.SetChildValue(context, Ua.BrowseNames.ReceiveTime, DateTimeUtc.Now, false);
            e.SetChildValue(
                context, Ua.BrowseNames.Severity, EffectiveSeverity(severity, tag), false);

            int count = Math.Min(fields.Count, tag.Fields.Count);
            for (int i = 0; i < count; i++)
            {
                e.SetChildValue(context, new QualifiedName(
                    tag.Fields[i].Name, m_manager.AssetNamespaceIndex), fields[i], false);
            }

            entry.Asset.ReportEvent(context, e);
        }

        private async ValueTask<AttributeSimpleReadResult> ReadFromProviderAsync(
            AssetEntry entry,
            WotPropertyTag tag,
            CancellationToken ct)
        {
            IWotAssetProvider? provider = entry.Provider;
            if (provider == null)
            {
                return new AttributeSimpleReadResult(StatusCodes.BadNotConnected, Variant.Null);
            }
            try
            {
                (ServiceResult status, Variant value) = await provider.ReadAsync(tag, ct).ConfigureAwait(false);
                return new AttributeSimpleReadResult(status, value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.ReadFailed(ex, entry.Name, tag.Name);
                return new AttributeSimpleReadResult(
                    ToClientStatus(ex, StatusCodes.BadCommunicationError, "Asset property read"),
                    Variant.Null);
            }
        }

        private async ValueTask<AttributeWriteResult> WriteToProviderAsync(
            AssetEntry entry,
            WotPropertyTag tag,
            Variant value,
            CancellationToken ct)
        {
            IWotAssetProvider? provider = entry.Provider;
            if (provider == null)
            {
                return new AttributeWriteResult(StatusCodes.BadNotConnected);
            }
            try
            {
                ServiceResult result = await provider.WriteAsync(tag, value, ct).ConfigureAwait(false);
                return new AttributeWriteResult(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.WriteFailed(ex, entry.Name, tag.Name);
                return new AttributeWriteResult(
                    ToClientStatus(ex, StatusCodes.BadCommunicationError, "Asset property write"));
            }
        }

        private async ValueTask<ServiceResult> InvokeActionAsync(
            AssetEntry entry,
            WotActionTag tag,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken ct)
        {
            IWotAssetProvider? provider = entry.Provider;
            if (provider == null)
            {
                return StatusCodes.BadNotConnected;
            }
            var inputCopy = new Variant[inputArguments.Count];
            for (int i = 0; i < inputArguments.Count; i++)
            {
                inputCopy[i] = inputArguments[i];
            }

            var outputBuffer = new Variant[tag.OutputArguments.Count];
            try
            {
                ServiceResult status = await provider.InvokeActionAsync(
                    tag, inputCopy, outputBuffer, ct).ConfigureAwait(false);

                outputArguments.Clear();
                for (int i = 0; i < outputBuffer.Length; i++)
                {
                    outputArguments.Add(outputBuffer[i]);
                }
                return status;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.ActionThrew(ex, tag.Name, entry.Name);
                return ToClientStatus(ex, StatusCodes.BadCommunicationError, "Asset action invocation");
            }
        }

        private void PersistTdToDisk(string name, ThingDescription td)
        {
            string? folder = m_options.ThingDescriptionStorageFolder;
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(folder);
                if (!WotAssetNameValidator.TryGetSafeFileName(name, folder!, out string? path))
                {
                    m_logger.RefusingToPersistTd(name, folder);
                    return;
                }
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                    td,
                    ThingDescriptionJsonContext.Default.ThingDescription);
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception ex)
            {
                m_logger.FailedToPersistTd(ex, name);
            }
        }

        private void DeleteTdFromDisk(string name)
        {
            string? folder = m_options.ThingDescriptionStorageFolder;
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            try
            {
                if (!WotAssetNameValidator.TryGetSafeFileName(name, folder!, out string? path))
                {
                    m_logger.RefusingToDeleteTd(name, folder);
                    return;
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToDeleteTd(ex, name);
            }
        }

        private async ValueTask MirrorToRegistryAsync(
            string name, ThingDescription td, CancellationToken ct)
        {
            IWotRegistryService? registry = m_options.RegistryBridge;
            if (registry is null)
            {
                return;
            }

            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                    td,
                    ThingDescriptionJsonContext.Default.ThingDescription);
                WotRegistryMutationResult result = await registry.UpsertResourceAsync(
                    new WotUpsertResourceRequest
                    {
                        GroupId = m_options.RegistryBridgeGroupId,
                        ResourceId = name,
                        Kind = WoTDocumentKindEnum.ThingDescription,
                        Content = ByteString.From(bytes),
                        ContentType = "application/td+json",
                        Format = "WoT-TD/1.1",
                        Name = name,
                        SetAsDefault = true
                    },
                    ct).ConfigureAwait(false);
                if (result.Outcome is WoTOutcomeEnum.Rejected or WoTOutcomeEnum.Failed)
                {
                    m_logger.RegistryBridgeMirrorRejected(name, result.Outcome, result.Message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.RegistryBridgeMirrorFailed(ex, name);
            }
        }

        private async ValueTask RemoveFromRegistryAsync(string name, CancellationToken ct)
        {
            IWotRegistryService? registry = m_options.RegistryBridge;
            if (registry is null)
            {
                return;
            }

            try
            {
                WotRegistryMutationResult result = await registry.DeleteResourceAsync(
                    m_options.RegistryBridgeGroupId,
                    name,
                    cancellationToken: ct).ConfigureAwait(false);
                if (result.Outcome is WoTOutcomeEnum.Rejected or WoTOutcomeEnum.Failed)
                {
                    m_logger.RegistryBridgeDeleteRejected(name, result.Outcome, result.Message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger.RegistryBridgeDeleteFailed(ex, name);
            }
        }

        /// <summary>
        /// Enumerates persisted thing descriptions from the storage folder,
        /// loading and deserialising each one.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// An async enumerable of tuples containing the asset name and its
        /// <see cref="ThingDescription"/>.
        /// </returns>
        public async IAsyncEnumerable<(string Name, ThingDescription Description)> EnumeratePersistedAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            string? folder = m_options.ThingDescriptionStorageFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                yield break;
            }

            int fileLimit = Math.Max(0, m_options.MaxPersistedThingDescriptionFiles);
            int sizeLimit = m_options.MaxThingDescriptionSize;
            ThingDescriptionJsonContext jsonContext = GetBoundedJsonContext();

            // A non-positive file limit acts as an explicit kill switch:
            // operators can drop the directory-load behaviour without
            // removing the folder by setting the option to 0.
            if (fileLimit == 0 && m_options.MaxPersistedThingDescriptionFiles <= 0)
            {
                m_logger.NoPersistedTdsLoaded(m_options.MaxPersistedThingDescriptionFiles, folder);
                yield break;
            }

            int processed = 0;
            foreach (string file in Directory.EnumerateFiles(folder, "*.jsonld"))
            {
                ct.ThrowIfCancellationRequested();

                if (processed >= fileLimit)
                {
                    m_logger.MaxPersistedTdFilesReached(fileLimit, folder);
                    yield break;
                }
                processed++;

                string name = Path.GetFileNameWithoutExtension(file);
                if (ServiceResult.IsBad(WotAssetNameValidator.Validate(name)))
                {
                    m_logger.SkippingPersistedTdNameValidation(file);
                    continue;
                }

                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (IOException ex)
                {
                    m_logger.SkippingPersistedTdMetadata(ex, file);
                    continue;
                }
                if (sizeLimit > 0 && size > sizeLimit)
                {
                    m_logger.SkippingPersistedTdTooLarge(file, size, sizeLimit);
                    continue;
                }

                ThingDescription? td;
                try
                {
                    using FileStream stream = File.OpenRead(file);
                    td = await JsonSerializer.DeserializeAsync(
                        stream,
                        jsonContext.ThingDescription,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (JsonException ex)
                {
                    m_logger.SkippingPersistedTdJsonDeserialization(ex, file, m_options.MaxThingDescriptionJsonDepth);
                    continue;
                }
                catch (IOException ex)
                {
                    m_logger.SkippingPersistedTdIoFailure(ex, file);
                    continue;
                }
                if (td != null)
                {
                    yield return (name, td);
                }
            }
        }

        /// <summary>
        /// Returns a <see cref="ThingDescriptionJsonContext"/> wired to a
        /// <see cref="JsonSerializerOptions"/> instance that enforces
        /// <see cref="WotConnectivityServerOptions.MaxThingDescriptionJsonDepth"/>.
        /// Falls back to the cached singleton context when the configured
        /// depth equals the global default so the hot-path startup case
        /// (no override) does not allocate a fresh context per call.
        /// </summary>
        private ThingDescriptionJsonContext GetBoundedJsonContext()
        {
            int depth = m_options.MaxThingDescriptionJsonDepth;
            if (depth <= 0 ||
                depth == ThingDescriptionJsonContext.Default.Options.MaxDepth)
            {
                return ThingDescriptionJsonContext.Default;
            }
            var options = new JsonSerializerOptions(
                ThingDescriptionJsonContext.Default.Options)
            {
                MaxDepth = depth
            };
            return new ThingDescriptionJsonContext(options);
        }

        /// <summary>
        /// Disposes all asset providers and releases associated resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            AssetEntry[] entries;
            lock (m_byName)
            {
                entries = [.. m_byNodeId.Values];
                m_byName.Clear();
                m_byNodeId.Clear();
            }
            foreach (AssetEntry entry in entries)
            {
                if (entry.Provider != null)
                {
                    try
                    {
                        await entry.Provider.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.ProviderForAssetThrewOnShutdown(ex, entry.Name);
                    }
                }
                entry.FileManager?.Dispose();
            }
            m_writeLock.Dispose();
        }

        /// <summary>
        /// Maps an exception thrown by a discovery / provider call to
        /// a client-facing <see cref="ServiceResult"/> that contains
        /// only the supplied generic operation name. Deliberately
        /// drops <c>ex.Message</c>, <c>ex.StackTrace</c>, and
        /// <c>ex.GetType().Name</c> so internal endpoint URIs,
        /// file-system paths, provider implementation details, and
        /// stack-trace fragments cannot leak to remote callers. The
        /// caller is responsible for logging the raw exception via
        /// <see cref="m_logger"/> at the corresponding site.
        /// </summary>
        /// <param name="ex">The thrown exception (unused; accepted so
        /// callers retain a single-line conversion).</param>
        /// <param name="status">The mapped <see cref="StatusCode"/>.</param>
        /// <param name="operation">A generic operation name surfaced
        /// to the client.</param>
        private static ServiceResult ToClientStatus(
            Exception ex, StatusCode status, string operation)
        {
            _ = ex;
            return ServiceResult.Create(status, "{0} failed.", operation);
        }

        /// <summary>
        /// Applies the asset-endpoint policy to the endpoints discovery returned.
        /// </summary>
        /// <remarks>
        /// WoT Connectivity §11 requires the same allowlist, trust policy and
        /// size limits that guard a caller-supplied <c>AssetEndpoint</c> to
        /// apply to the endpoints <c>DiscoverAssets</c> probes, because it
        /// probes on the server's own initiative and returns the outcome to the
        /// caller. Handing back an endpoint the policy forbids would make the
        /// server a network probe for a caller that could not reach it directly.
        /// </remarks>
        /// <param name="endpoints">The endpoints discovery reported.</param>
        /// <returns>The endpoints the policy permits.</returns>
        private IReadOnlyList<string> FilterDiscoveredEndpoints(
            IReadOnlyList<string> endpoints)
        {
            if (endpoints.Count == 0)
            {
                return endpoints;
            }
            var permitted = new List<string>(endpoints.Count);
            foreach (string endpoint in endpoints)
            {
                if (ServiceResult.IsGood(AssetEndpointValidator.Validate(
                        endpoint, m_options.AssetEndpointPolicy, out Uri? normalized)) &&
                    normalized is not null)
                {
                    permitted.Add(normalized.AbsoluteUri);
                }
            }
            if (permitted.Count != endpoints.Count)
            {
                m_logger.DiscoveredEndpointsFilteredByPolicy(
                    endpoints.Count - permitted.Count);
            }
            return permitted;
        }

        /// <summary>
        /// Returns the conventional WoT status code for the supplied
        /// exception. Mapping:
        ///   <see cref="NotSupportedException"/>       => Bad_NotSupported
        ///   <see cref="ArgumentException"/>            => Bad_InvalidArgument
        ///   <see cref="IOException"/>                  => Bad_ResourceUnavailable
        ///   anything else                              => Bad_InternalError
        /// <see cref="OperationCanceledException"/> is **never** mapped:
        /// callers must put it in a <c>when</c>-filter so it propagates.
        /// </summary>
        private static StatusCode MapToStatusCode(Exception ex)
        {
            return ex switch
            {
                NotSupportedException => StatusCodes.BadNotSupported,
                ArgumentException => StatusCodes.BadInvalidArgument,
                IOException => StatusCodes.BadResourceUnavailable,
                _ => StatusCodes.BadInternalError
            };
        }

        /// <summary>
        /// The single subscriber id the registry uses for provider event
        /// subscriptions: the asset raises every occurrence for the lifetime
        /// of the TD generation rather than once per interested client.
        /// </summary>
        private const uint EventSubscriberId = 0;

        private readonly WotConnectivityNodeManager m_manager;
        private readonly WotConnectivityServerOptions m_options;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_writeLock = new(1, 1);
        private readonly Dictionary<string, AssetEntry> m_byName = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeId, AssetEntry> m_byNodeId = [];
    }

    internal static partial class AssetRegistryLog
    {
        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 37, Level = LogLevel.Warning,
            Message = "Skipping duplicate TD event '{ChildName}' for asset {AssetName}.")]
        public static partial void SkippingDuplicateTdEvent(this ILogger logger, string childName, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 38, Level = LogLevel.Warning,
            Message = "Failed to subscribe asset {AssetName} event '{EventName}'.")]
        public static partial void EventSubscribeFailed(
            this ILogger logger, Exception exception, string assetName, string eventName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 39, Level = LogLevel.Warning,
            Message = "Failed to unsubscribe asset {AssetName} event '{EventName}'.")]
        public static partial void EventUnsubscribeFailed(
            this ILogger logger, Exception exception, string assetName, string eventName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 0, Level = LogLevel.Warning,
            Message = "Provider for asset {AssetName} threw on disposal")]
        public static partial void ProviderForAssetThrewOnDisposal(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 1, Level = LogLevel.Warning,
            Message = "CreateAssetForEndpoint rejected by AssetEndpointPolicy: {Status}")]
        public static partial void CreateAssetForEndpointRejected(this ILogger logger, StatusCode status);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 2, Level = LogLevel.Error,
            Message = "CreateAssetForEndpoint failed for asset {AssetName}: provider rejected the endpoint")]
        public static partial void CreateAssetForEndpointProviderRejected(
            this ILogger logger,
            Exception ex,
            string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 3, Level = LogLevel.Warning,
            Message = "CreateAssetForEndpoint timed out after {Timeout} for {AssetName}")]
        public static partial void CreateAssetForEndpointTimedOut(
            this ILogger logger,
            Exception ex,
            TimeSpan timeout,
            string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 4, Level = LogLevel.Error,
            Message = "CreateAssetForEndpoint failed for asset {AssetName}")]
        public static partial void CreateAssetForEndpointFailed(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 5, Level = LogLevel.Warning,
            Message = "DiscoverAssets not supported by configured provider")]
        public static partial void DiscoverAssetsNotSupported(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 6, Level = LogLevel.Error,
            Message = "DiscoverAssets failed")]
        public static partial void DiscoverAssetsFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 35, Level = LogLevel.Warning,
            Message = "DiscoverAssets withheld {Count} endpoint(s) the asset endpoint policy forbids")]
        public static partial void DiscoveredEndpointsFilteredByPolicy(
            this ILogger logger, int count);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 36, Level = LogLevel.Warning,
            Message = "Thing description generated for asset {AssetName} failed format validation " +
                "and materialized nothing")]
        public static partial void GeneratedThingDescriptionFailedFormatValidation(
            this ILogger logger, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 7, Level = LogLevel.Warning,
            Message = "ConnectionTest rejected by AssetEndpointPolicy: {Status}")]
        public static partial void ConnectionTestRejected(this ILogger logger, StatusCode status);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 8, Level = LogLevel.Warning,
            Message = "ConnectionTest not supported by configured provider")]
        public static partial void ConnectionTestNotSupported(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 9, Level = LogLevel.Error,
            Message = "ConnectionTest failed for endpoint provided by client")]
        public static partial void ConnectionTestFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 10, Level = LogLevel.Warning,
            Message = "ConnectionTest timed out after {Timeout}")]
        public static partial void ConnectionTestTimedOut(this ILogger logger, Exception ex, TimeSpan timeout);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 11, Level = LogLevel.Error,
            Message = "Binding factory {Factory} failed to connect asset {AssetName}")]
        public static partial void BindingFactoryFailedToConnectAsset(
            this ILogger logger,
            Exception ex,
            string factory,
            string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 12, Level = LogLevel.Warning,
            Message = "Previous provider for {AssetName} threw on disposal")]
        public static partial void PreviousProviderThrewOnDisposal(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 13, Level = LogLevel.Warning,
            Message = "Skipping duplicate TD property '{ChildName}' for asset {AssetName}.")]
        public static partial void SkippingDuplicateTdProperty(this ILogger logger, string childName, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 14, Level = LogLevel.Warning,
            Message = "Skipping duplicate TD action '{ChildName}' for asset {AssetName}.")]
        public static partial void SkippingDuplicateTdAction(this ILogger logger, string childName, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 15, Level = LogLevel.Warning,
            Message = "Skipping TD {Kind} '{ChildName}' on asset {AssetName}: {Reason}")]
        public static partial void SkippingTd(
            this ILogger logger,
            string kind,
            string childName,
            string assetName,
            string? reason);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 16, Level = LogLevel.Warning,
            Message = "Read failed for asset {AssetName} property {Property}")]
        public static partial void ReadFailed(this ILogger logger, Exception ex, string assetName, string property);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 17, Level = LogLevel.Warning,
            Message = "Write failed for asset {AssetName} property {Property}")]
        public static partial void WriteFailed(this ILogger logger, Exception ex, string assetName, string property);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 18, Level = LogLevel.Warning,
            Message = "Action {Action} on asset {AssetName} threw")]
        public static partial void ActionThrew(this ILogger logger, Exception ex, string action, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 19, Level = LogLevel.Warning,
            Message = "Refusing to persist TD for asset {AssetName}: name did not resolve to a safe path " +
                "under {Folder}.")]
        public static partial void RefusingToPersistTd(this ILogger logger, string assetName, string folder);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 20, Level = LogLevel.Warning,
            Message = "Failed to persist TD for asset {AssetName}")]
        public static partial void FailedToPersistTd(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 21, Level = LogLevel.Warning,
            Message = "Refusing to delete TD for asset {AssetName}: name did not resolve to a safe path " +
                "under {Folder}.")]
        public static partial void RefusingToDeleteTd(this ILogger logger, string assetName, string folder);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 22, Level = LogLevel.Warning,
            Message = "Failed to delete TD for asset {AssetName}")]
        public static partial void FailedToDeleteTd(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 23, Level = LogLevel.Information,
            Message = "MaxPersistedThingDescriptionFiles is {Limit}; no persisted TDs will be loaded from {Folder}.")]
        public static partial void NoPersistedTdsLoaded(this ILogger logger, int limit, string folder);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 24, Level = LogLevel.Warning,
            Message = "Reached MaxPersistedThingDescriptionFiles ({Limit}); skipping the remaining " +
                "persisted TDs in {Folder}.")]
        public static partial void MaxPersistedTdFilesReached(this ILogger logger, int limit, string folder);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 25, Level = LogLevel.Warning,
            Message = "Skipping persisted TD {File}: name does not pass asset-name validation.")]
        public static partial void SkippingPersistedTdNameValidation(this ILogger logger, string file);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 26, Level = LogLevel.Warning,
            Message = "Skipping persisted TD {File}: file metadata could not be read.")]
        public static partial void SkippingPersistedTdMetadata(this ILogger logger, Exception ex, string file);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 27, Level = LogLevel.Warning,
            Message = "Skipping persisted TD {File}: size {Bytes} exceeds MaxThingDescriptionSize ({Limit}).")]
        public static partial void SkippingPersistedTdTooLarge(
            this ILogger logger,
            string file,
            long bytes,
            long limit);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 28, Level = LogLevel.Warning,
            Message = "Skipping persisted TD {File}: JSON deserialization failed (likely exceeds " +
                "MaxThingDescriptionJsonDepth={Depth} or is otherwise malformed).")]
        public static partial void SkippingPersistedTdJsonDeserialization(
            this ILogger logger,
            Exception ex,
            string file,
            int depth);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 29, Level = LogLevel.Warning,
            Message = "Skipping persisted TD {File}: I/O failure while reading.")]
        public static partial void SkippingPersistedTdIoFailure(this ILogger logger, Exception ex, string file);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 30, Level = LogLevel.Warning,
            Message = "Provider for asset {AssetName} threw on shutdown")]
        public static partial void ProviderForAssetThrewOnShutdown(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 31, Level = LogLevel.Warning,
            Message = "Registry bridge rejected mirrored Thing Description for asset {AssetName}: {Outcome} {Message}")]
        public static partial void RegistryBridgeMirrorRejected(
            this ILogger logger,
            string assetName,
            WoTOutcomeEnum outcome,
            string message);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 32, Level = LogLevel.Warning,
            Message = "Registry bridge failed to mirror Thing Description for asset {AssetName}")]
        public static partial void RegistryBridgeMirrorFailed(this ILogger logger, Exception ex, string assetName);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 33, Level = LogLevel.Warning,
            Message = "Registry bridge rejected resource delete for asset {AssetName}: {Outcome} {Message}")]
        public static partial void RegistryBridgeDeleteRejected(
            this ILogger logger,
            string assetName,
            WoTOutcomeEnum outcome,
            string message);

        [LoggerMessage(EventId = WotConServerEventIds.AssetRegistry + 34, Level = LogLevel.Warning,
            Message = "Registry bridge failed to delete resource for asset {AssetName}")]
        public static partial void RegistryBridgeDeleteFailed(this ILogger logger, Exception ex, string assetName);
    }
}
