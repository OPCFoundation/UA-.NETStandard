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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Assets;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// OPC UA node manager that exposes the WoT Connectivity model
    /// (OPC 10100-1) and its dynamic assets.
    /// </summary>
    /// <remarks>
    /// The static model nodes — <c>WoTAssetConnectionManagement</c>, all
    /// type definitions, and the <c>HasWoTComponent</c> reference type —
    /// are loaded from the generated <c>AddOpcUaWotCon</c> table. Dynamic
    /// nodes (assets, property variables, action methods) are created
    /// per asset by <see cref="AssetRegistry"/> in a dedicated namespace
    /// (<see cref="WotConnectivityServerOptions.AssetNamespaceUri"/>).
    /// </remarks>
    public sealed class WotConnectivityNodeManager : AsyncCustomNodeManager, INodeIdFactory
    {
        /// <summary>
        /// Initialises a new <see cref="WotConnectivityNodeManager"/>.
        /// </summary>
        public WotConnectivityNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            WotConnectivityServerOptions options)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<WotConnectivityNodeManager>(),
                  options?.AssetNamespaceUri ?? throw new ArgumentNullException(nameof(options)),
                  Namespaces.WotCon)
        {
            m_options = options;
            SystemContext.NodeIdFactory = this;
            AssetNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(options.AssetNamespaceUri);
            WotConNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(Namespaces.WotCon);
            m_registry = new AssetRegistry(this, options, m_logger);
        }

        /// <summary>The namespace index used for dynamically created asset nodes.</summary>
        public ushort AssetNamespaceIndex { get; }

        /// <summary>The namespace index of the WoT Connectivity model.</summary>
        public ushort WotConNamespaceIndex { get; }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (!node.NodeId.IsNull)
            {
                return node.NodeId;
            }
            return new NodeId((uint)Interlocked.Increment(ref m_nextDynamicId), AssetNamespaceIndex);
        }

        /// <summary>
        /// Builds a stable string NodeId for a dynamic child of an asset.
        /// </summary>
        internal NodeId AllocateChildNodeId(string assetName, string category, string childName)
        {
            return new NodeId($"Assets/{assetName}/{category}/{childName}", AssetNamespaceIndex);
        }

        /// <summary>Creates an asset object below the management node.</summary>
        internal async ValueTask<AssetEntry> CreateAssetNodeAsync(
            string assetName,
            CancellationToken ct)
        {
            await m_writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var asset = new IWoTAssetState(m_managementObject)
                {
                    NodeId = new NodeId($"Assets/{assetName}", AssetNamespaceIndex),
                    SymbolicName = assetName,
                    BrowseName = new QualifiedName(assetName, AssetNamespaceIndex),
                    DisplayName = new LocalizedText(assetName),
                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                    TypeDefinitionId = Ua.ObjectTypeIds.BaseObjectType
                };
                asset.Create(SystemContext, asset.NodeId, asset.BrowseName, asset.DisplayName, true);
                asset.AddReference(Ua.ReferenceTypeIds.HasInterface, isInverse: false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IWoTAssetType, Server.NamespaceUris));

                if (asset.WoTFile != null)
                {
                    asset.WoTFile.NodeId = new NodeId($"Assets/{assetName}/File", AssetNamespaceIndex);
                    asset.WoTFile.BrowseName = new QualifiedName("WoTFile", AssetNamespaceIndex);
                    asset.WoTFile.DisplayName = new LocalizedText("WoTFile");
                    AssignChildNodeIds(asset.WoTFile, $"Assets/{assetName}/File");
                }

                m_managementObject!.AddChild(asset);
                m_managementObject.AddReference(Ua.ReferenceTypeIds.Organizes, isInverse: false, asset.NodeId);
                asset.AddReference(Ua.ReferenceTypeIds.Organizes, isInverse: true, m_managementObject.NodeId);

                await AddPredefinedNodeAsync(SystemContext, asset, ct).ConfigureAwait(false);
                return new AssetEntry(assetName, asset);
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        /// <summary>Removes an asset object and all its children.</summary>
        internal async ValueTask DeleteAssetNodeAsync(IWoTAssetState asset, CancellationToken ct)
        {
            await m_writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                m_managementObject?.RemoveReference(
                    Ua.ReferenceTypeIds.Organizes, isInverse: false, asset.NodeId);
                m_managementObject?.RemoveChild(asset);
                await DeleteNodeAsync(SystemContext, asset.NodeId, ct).ConfigureAwait(false);
            }
            finally
            {
                m_writeLock.Release();
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUaWotCon(context));
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            if (predefinedNode is BaseObjectState passive &&
                passive.TypeDefinitionId == ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.WoTAssetConnectionManagementType,
                    Server.NamespaceUris))
            {
                WoTAssetConnectionManagementState active = passive as WoTAssetConnectionManagementState
                    ?? Promote(context, passive);

                WireManagementMethods(active);
                ApplyConfiguration(context, active);
                m_managementObject = active;
                return new ValueTask<NodeState>(active);
            }

            return new ValueTask<NodeState>(predefinedNode);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // Reload any persisted assets so they survive restarts.
            await foreach ((string name, ThingDescription td) in
                m_registry.EnumeratePersistedAsync(cancellationToken).ConfigureAwait(false))
            {
                (ServiceResult create, NodeId assetId) = await m_registry
                    .CreateAssetAsync(name, cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(create))
                {
                    m_logger.LogWarning("Restoring asset {AssetName} failed: {Status}",
                        name, create);
                    continue;
                }
                AssetEntry? entry = m_registry.FindByNodeId(assetId);
                if (entry != null)
                {
                    await m_registry.RebuildAsync(entry, td, persistOnSuccess: false, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            await m_registry.DisposeAsync().ConfigureAwait(false);
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_registry.DisposeAsync().AsTask().GetAwaiter().GetResult();
                m_writeLock.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (monitoredItem.MonitoringMode == MonitoringMode.Disabled ||
                handle.Node is not BaseDataVariableState source)
            {
                return;
            }
            StartSubscription(source, monitoredItem.Id);
        }

        /// <inheritdoc/>
        protected override ValueTask OnMonitoredItemModifiedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            // For modify we restart the observation so the new sampling
            // interval (or filter) propagates if the provider honours it.
            if (handle.Node is BaseDataVariableState source)
            {
                StopSubscription(source, monitoredItem.Id);
                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    StartSubscription(source, monitoredItem.Id);
                }
            }
            return new ValueTask();
        }

        /// <inheritdoc/>
        protected override ValueTask OnMonitoredItemDeletedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            if (handle.Node is BaseDataVariableState source)
            {
                StopSubscription(source, monitoredItem.Id);
            }
            return new ValueTask();
        }

        /// <inheritdoc/>
        protected override ValueTask OnMonitoringModeChangedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken = default)
        {
            if (handle.Node is not BaseDataVariableState source)
            {
                return new ValueTask();
            }
            if (previousMode != MonitoringMode.Disabled && monitoringMode == MonitoringMode.Disabled)
            {
                StopSubscription(source, monitoredItem.Id);
            }
            else if (previousMode == MonitoringMode.Disabled && monitoringMode != MonitoringMode.Disabled)
            {
                StartSubscription(source, monitoredItem.Id);
            }
            return new ValueTask();
        }

        private void StartSubscription(BaseDataVariableState source, uint subscriberId)
        {
            if (!m_registry.TryGetProperty(source.NodeId,
                    out AssetEntry entry, out BaseDataVariableState variable, out WotPropertyTag tag) ||
                entry.Provider == null ||
                !tag.Observable)
            {
                return;
            }
            void OnChange(WotPropertyTag t, Variant value, StatusCode status, DateTime timestamp)
            {
                OnProviderValueChange(variable, value, status, timestamp);
            }
            lock (entry.SubscriberCallbacks)
            {
                entry.SubscriberCallbacks[subscriberId] = OnChange;
            }
            try
            {
                entry.Provider.SubscribeAsync(tag, subscriberId, OnChange, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Subscribe failed for asset {AssetName} property {Property}",
                    entry.Name, tag.Name);
            }
        }

        private void StopSubscription(BaseDataVariableState source, uint subscriberId)
        {
            if (!m_registry.TryGetProperty(source.NodeId,
                    out AssetEntry entry, out _, out WotPropertyTag tag) ||
                entry.Provider == null)
            {
                return;
            }
            lock (entry.SubscriberCallbacks)
            {
                entry.SubscriberCallbacks.Remove(subscriberId);
            }
            try
            {
                entry.Provider.UnsubscribeAsync(tag, subscriberId, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Unsubscribe failed for asset {AssetName} property {Property}",
                    entry.Name, tag.Name);
            }
        }

        private void OnProviderValueChange(
            BaseDataVariableState variable,
            Variant value,
            StatusCode status,
            DateTime timestamp)
        {
            lock (m_changeLock)
            {
                variable.Value = value;
                variable.StatusCode = status;
                variable.Timestamp = timestamp;
                variable.ClearChangeMasks(SystemContext, includeChildren: false);
            }
        }

        private WoTAssetConnectionManagementState Promote(ISystemContext context, BaseObjectState passive)
        {
            var active = new WoTAssetConnectionManagementState(passive.Parent);
            active.Create(context, passive);
            passive.Parent?.ReplaceChild(context, active);
            return active;
        }

        private void WireManagementMethods(WoTAssetConnectionManagementState management)
        {
            if (management.CreateAsset != null)
            {
                management.CreateAsset.OnCallAsync = OnCreateAssetAsync;
            }
            if (management.DeleteAsset != null)
            {
                management.DeleteAsset.OnCallAsync = OnDeleteAssetAsync;
            }
            management.DiscoverAssets ??= management.AddDiscoverAssets(SystemContext);
            management.DiscoverAssets.OnCallAsync = OnDiscoverAssetsAsync;

            management.CreateAssetForEndpoint ??= management.AddCreateAssetForEndpoint(SystemContext);
            management.CreateAssetForEndpoint.OnCallAsync = OnCreateAssetForEndpointAsync;

            management.ConnectionTest ??= management.AddConnectionTest(SystemContext);
            management.ConnectionTest.OnCallAsync = OnConnectionTestAsync;

            management.SupportedWoTBindings ??= management.AddSupportedWoTBindings(SystemContext);
            var bindings = new List<string>();
            foreach (IWotAssetProviderFactory factory in m_options.Bindings)
            {
                foreach (string binding in factory.SupportedBindings)
                {
                    if (!bindings.Contains(binding))
                    {
                        bindings.Add(binding);
                    }
                }
            }
            management.SupportedWoTBindings.Value = new ArrayOf<string>(bindings.ToArray());
        }

        private void ApplyConfiguration(ISystemContext context, WoTAssetConnectionManagementState management)
        {
            if (m_options.Configuration.Count == 0 && string.IsNullOrEmpty(m_options.License))
            {
                return;
            }
            WoTAssetConfigurationState configuration = management.Configuration
                ?? management.AddConfiguration(context);
            management.Configuration = configuration;

            if (!string.IsNullOrEmpty(m_options.License))
            {
                configuration.License ??= configuration.AddLicense(context);
                configuration.License.Value = m_options.License!;
            }

            foreach (KeyValuePair<string, WotConfigurationParameter> kv in m_options.Configuration)
            {
                PropertyState child = configuration.AddWoTConfigurationParameterName_Placeholder(
                    context,
                    new QualifiedName(kv.Key, WotConNamespaceIndex));
                child.DataType = kv.Value.DataType;
                child.ValueRank = ValueRanks.Scalar;
                child.AccessLevel = kv.Value.Writable ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;
                child.UserAccessLevel = child.AccessLevel;
                child.Value = kv.Value.InitialValue
                    ?? TypeInfo.GetDefaultVariantValue(kv.Value.DataType, ValueRanks.Scalar);
                if (!string.IsNullOrEmpty(kv.Value.Description))
                {
                    child.Description = new LocalizedText(kv.Value.Description);
                }
            }
        }

        private async ValueTask<CreateAssetMethodStateResult> OnCreateAssetAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string assetName,
            CancellationToken cancellationToken)
        {
            (ServiceResult status, NodeId assetId) = await m_registry
                .CreateAssetAsync(assetName, cancellationToken).ConfigureAwait(false);
            return new CreateAssetMethodStateResult { ServiceResult = status, AssetId = assetId.IsNull ? NodeId.Null : assetId };
        }

        private async ValueTask<DeleteAssetMethodStateResult> OnDeleteAssetAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId assetId,
            CancellationToken cancellationToken)
        {
            ServiceResult status = await m_registry
                .DeleteAssetAsync(assetId, cancellationToken).ConfigureAwait(false);
            return new DeleteAssetMethodStateResult { ServiceResult = status };
        }

        private async ValueTask<DiscoverAssetsMethodStateResult> OnDiscoverAssetsAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            CancellationToken cancellationToken)
        {
            (ServiceResult status, IReadOnlyList<string> endpoints) = await m_registry
                .DiscoverAssetsAsync(cancellationToken).ConfigureAwait(false);
            string[] arr = new string[endpoints.Count];
            for (int i = 0; i < endpoints.Count; i++)
            {
                arr[i] = endpoints[i];
            }
            return new DiscoverAssetsMethodStateResult
            {
                ServiceResult = status,
                AssetEndpoints = new ArrayOf<string>(arr)
            };
        }

        private async ValueTask<CreateAssetForEndpointMethodStateResult> OnCreateAssetForEndpointAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string assetName,
            string assetEndpoint,
            CancellationToken cancellationToken)
        {
            (ServiceResult status, NodeId assetId) = await m_registry
                .CreateAssetForEndpointAsync(assetName, assetEndpoint, cancellationToken)
                .ConfigureAwait(false);
            return new CreateAssetForEndpointMethodStateResult
            {
                ServiceResult = status,
                AssetId = assetId.IsNull ? NodeId.Null : assetId
            };
        }

        private async ValueTask<ConnectionTestMethodStateResult> OnConnectionTestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string assetEndpoint,
            CancellationToken cancellationToken)
        {
            (ServiceResult status, bool success, string text) = await m_registry
                .ConnectionTestAsync(assetEndpoint, cancellationToken).ConfigureAwait(false);
            return new ConnectionTestMethodStateResult
            {
                ServiceResult = status,
                Success = success,
                Status = text
            };
        }

        private void AssignChildNodeIds(NodeState node, string parentPath)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                child.NodeId = new NodeId(
                    parentPath + "/" + child.BrowseName.Name,
                    AssetNamespaceIndex);
                AssignChildNodeIds(child, parentPath + "/" + child.BrowseName.Name);
            }
        }

        private readonly WotConnectivityServerOptions m_options;
        private readonly AssetRegistry m_registry;
        private readonly SemaphoreSlim m_writeLock = new(1, 1);
        private readonly object m_changeLock = new();
        private WoTAssetConnectionManagementState? m_managementObject;
        private long m_nextDynamicId = 1_000_000;
    }
}
