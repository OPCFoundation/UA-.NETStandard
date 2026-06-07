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
                    m_logger);

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
                        m_logger.LogWarning(ex,
                            "Provider for asset {AssetName} threw on disposal", entry.Name);
                    }
                }
                entry.FileManager?.Dispose();

                await m_manager.DeleteAssetNodeAsync(entry.Asset, ct).ConfigureAwait(false);
                DeleteTdFromDisk(entry.Name);
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
            (ServiceResult createResult, NodeId assetId) = await CreateAssetAsync(assetName, ct)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(createResult))
            {
                return (createResult, assetId);
            }
            try
            {
                ThingDescription td = await m_options.Discovery
                    .CreateThingDescriptionAsync(assetName, assetEndpoint, ct)
                    .ConfigureAwait(false);
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
                return (ServiceResult.Create(ex, StatusCodes.BadNotSupported, ex.Message), NodeId.Null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await DeleteAssetAsync(assetId, ct).ConfigureAwait(false);
                m_logger.LogError(ex, "CreateAssetForEndpoint failed for {AssetName}", assetName);
                return (ServiceResult.Create(ex, StatusCodes.BadConfigurationError, ex.Message), NodeId.Null);
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
                return (ServiceResult.Good, endpoints);
            }
            catch (NotSupportedException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadNotSupported, ex.Message),
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
            try
            {
                (bool success, string status) = await m_options.Discovery
                    .TestAsync(assetEndpoint, ct).ConfigureAwait(false);
                return (ServiceResult.Good, success, status ?? string.Empty);
            }
            catch (NotSupportedException ex)
            {
                return (ServiceResult.Create(ex, StatusCodes.BadNotSupported, ex.Message), false, string.Empty);
            }
        }

        /// <summary>
        /// Rebuilds the variable + method children of an asset from a TD,
        /// reconnecting (or replacing) its provider as appropriate.
        /// </summary>
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
                m_logger.LogError(ex,
                    "Binding factory {Factory} failed to connect asset {AssetName}",
                    factory.GetType().Name, entry.Name);
                return ServiceResult.Create(ex, StatusCodes.BadConfigurationError, ex.Message);
            }

            await m_writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (entry.Provider != null)
                {
                    try
                    {
                        await entry.Provider.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(ex,
                            "Previous provider for {AssetName} threw on disposal", entry.Name);
                    }
                }
                entry.Provider = provider;

                ClearDynamicChildren(entry);

                if (td.Properties != null)
                {
                    foreach (KeyValuePair<string, WotProperty> kv in td.Properties)
                    {
                        BuildPropertyNode(entry, kv.Key, kv.Value);
                    }
                }
                if (td.Actions != null)
                {
                    foreach (KeyValuePair<string, WotAction> kv in td.Actions)
                    {
                        BuildActionNode(entry, kv.Key, kv.Value);
                    }
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
            }
            finally
            {
                m_writeLock.Release();
            }
            return ServiceResult.Good;
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
                DataType = mapped ? dataType : DataTypeIds.BaseDataType,
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
                inputProperty.DataType = DataTypeIds.Argument;
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
                outputProperty.DataType = DataTypeIds.Argument;
                outputProperty.ValueRank = ValueRanks.OneDimension;
                outputProperty.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
                outputProperty.TypeDefinitionId = VariableTypeIds.PropertyType;
                outputProperty.Value = new ArrayOf<Argument>(argsArray);
                method.OutputArguments = outputProperty;
                method.AddChild(outputProperty);
            }

            JsonElement? form = action.Forms?.Count > 0 ? action.Forms[0] : null;
            var tag = new WotActionTag(name, nodeId, inputArgs, outputArgs, form);

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
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Read failed for asset {AssetName} property {Property}", entry.Name, tag.Name);
                return new AttributeSimpleReadResult(
                    ServiceResult.Create(ex, StatusCodes.BadCommunicationError, ex.Message),
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
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Write failed for asset {AssetName} property {Property}", entry.Name, tag.Name);
                return new AttributeWriteResult(
                    ServiceResult.Create(ex, StatusCodes.BadCommunicationError, ex.Message));
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
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Action {Action} on asset {AssetName} threw", tag.Name, entry.Name);
                return ServiceResult.Create(ex, StatusCodes.BadCommunicationError, ex.Message);
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
                    m_logger.LogWarning(
                        "Refusing to persist TD for asset {AssetName}: name did not resolve to a safe path under {Folder}.",
                        name, folder);
                    return;
                }
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                    td,
                    ThingDescriptionJsonContext.Default.ThingDescription);
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to persist TD for asset {AssetName}", name);
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
                    m_logger.LogWarning(
                        "Refusing to delete TD for asset {AssetName}: name did not resolve to a safe path under {Folder}.",
                        name, folder);
                    return;
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to delete TD for asset {AssetName}", name);
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
            foreach (string file in Directory.EnumerateFiles(folder, "*.jsonld"))
            {
                ct.ThrowIfCancellationRequested();
                string name = Path.GetFileNameWithoutExtension(file);
                if (ServiceResult.IsBad(WotAssetNameValidator.Validate(name)))
                {
                    m_logger.LogWarning(
                        "Skipping persisted TD {File}: name does not pass asset-name validation.",
                        file);
                    continue;
                }
                ThingDescription? td;
                try
                {
                    using FileStream stream = File.OpenRead(file);
                    td = await JsonSerializer.DeserializeAsync(
                        stream,
                        ThingDescriptionJsonContext.Default.ThingDescription,
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to load persisted TD {File}", file);
                    continue;
                }
                if (td != null)
                {
                    yield return (name, td);
                }
            }
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
                        m_logger.LogWarning(ex,
                            "Provider for asset {AssetName} threw on shutdown", entry.Name);
                    }
                }
                entry.FileManager?.Dispose();
            }
            m_writeLock.Dispose();
        }

        private readonly WotConnectivityNodeManager m_manager;
        private readonly WotConnectivityServerOptions m_options;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_writeLock = new(1, 1);
        private readonly Dictionary<string, AssetEntry> m_byName = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeId, AssetEntry> m_byNodeId = [];
    }
}
