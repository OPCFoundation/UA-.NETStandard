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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.AMB.Server.Configuration;
using Opc.Ua.AMB.Server.DocumentationLinks;
using Opc.Ua.AMB.Server.Health;
using Opc.Ua.AMB.Server.Maintenance;
using Opc.Ua.AMB.Server.Structure;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// The registry of the manageable assets of a server.
    /// </summary>
    /// <remarks>
    /// One instance is shared by the Asset Management Basics node manager and
    /// every node manager that registers assets. Hosting registers it as a
    /// singleton through <c>AddAssetManagement</c>; without hosting, create
    /// it and hand it to the <see cref="AmbNodeManager"/> constructor.
    /// </remarks>
    public sealed class AssetManagement : IAssetManagement
    {
        /// <summary>
        /// Creates the registry.
        /// </summary>
        /// <param name="options">The options; defaults when <see langword="null"/>.</param>
        /// <param name="store">
        /// Persists what clients configure on the assets; a
        /// <see cref="MemoryAssetConfigurationStore"/> when <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentException">The options are invalid.</exception>
        public AssetManagement(AmbServerOptions? options = null, IAssetConfigurationStore? store = null)
        {
            Options = options ?? new AmbServerOptions();
            Options.Validate();
            Store = store ?? new MemoryAssetConfigurationStore();
        }

        /// <summary>
        /// Gets the options of the asset management.
        /// </summary>
        public AmbServerOptions Options { get; }

        /// <summary>
        /// Gets the store that persists what clients configure on the assets.
        /// </summary>
        public IAssetConfigurationStore Store { get; }

        /// <inheritdoc/>
        public ArrayOf<IAssetHandle> Assets
        {
            get
            {
                lock (m_lock)
                {
                    var assets = new IAssetHandle[m_order.Count];
                    for (int ii = 0; ii < m_order.Count; ii++)
                    {
                        assets[ii] = m_order[ii];
                    }
                    return assets.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the node manager the registry serves, once it exists.
        /// </summary>
        internal AmbNodeManager? Manager
        {
            get
            {
                lock (m_lock)
                {
                    return m_manager;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IAssetHandle> RegisterAssetAsync(
            INodeBuilder<BaseObjectState> asset,
            Action<IAssetBuilder>? configure = null,
            CancellationToken cancellationToken = default)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            AmbNodeManager manager = Manager ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Asset Management Basics node manager is not part of the server. " +
                    "Register it with AddAssetManagement() or construct an AmbNodeManager with this registry.");
            if (asset.Node is not BaseObjectState node)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Asset '{0}' must be an Object.",
                    asset.Node?.BrowseName ?? QualifiedName.Null);
            }
            int diNamespaceIndex = manager.Server.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            if (diNamespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "OPC 10000-110 identifies assets through OPC 10000-100, but the server does not " +
                    "publish the Device Integration namespace. Register AddOpcUaDi() or a companion " +
                    "specification built on it.");
            }

            var builder = new AssetBuilder();
            configure?.Invoke(builder);

            var handle = new AssetHandle(
                this,
                node,
                asset.Builder.NodeManager,
                asset.Builder.Context,
                (ushort)diNamespaceIndex);

            // The NodeId is reserved for the duration of the registration, so
            // the duplicate check also covers the awaits in between: binding
            // the AssetId reads the store.
            lock (m_lock)
            {
                if (m_assets.ContainsKey(node.NodeId) || !m_pending.Add(node.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdExists,
                        "Asset '{0}' is already registered.",
                        node.BrowseName);
                }
            }
            try
            {
                string productInstanceUri = handle.ProductInstanceUri;
                if (productInstanceUri.Length == 0 && Options.RequireProductInstanceUri)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Asset '{0}' publishes no ProductInstanceUri, which OPC 10000-110 §7 requires of " +
                        "every manageable asset. Set it, directly or in the Identification group, before " +
                        "registering the asset.",
                        node.BrowseName);
                }

                if (builder.ConfigurableAssetId)
                {
                    handle.ConfigurableAssetIdBinding = await ConfigurableAssetId.BindAsync(
                        handle.Owner,
                        handle.Context,
                        node,
                        handle.DiNamespaceIndex,
                        productInstanceUri,
                        builder.DefaultAssetId,
                        Store,
                        Options.MaxAssetIdLength,
                        Logger,
                        cancellationToken).ConfigureAwait(false);
                }

                handle.Health = await AssetHealth.CreateAsync(
                    manager,
                    asset,
                    handle,
                    builder,
                    Logger,
                    cancellationToken).ConfigureAwait(false);
                handle.Maintenance = await AssetMaintenance.CreateAsync(
                    manager,
                    asset,
                    handle,
                    builder,
                    Logger,
                    cancellationToken).ConfigureAwait(false);
                handle.DocumentationLinks = await AssetDocumentationLinks.CreateAsync(
                    manager,
                    handle,
                    builder.DocumentationLinks,
                    Options,
                    Store,
                    Logger,
                    cancellationToken).ConfigureAwait(false);
                handle.Structure = await AssetStructure.ApplyAsync(
                    manager,
                    handle,
                    builder.Structure,
                    Store,
                    Options,
                    Logger,
                    cancellationToken).ConfigureAwait(false);

                lock (m_lock)
                {
                    m_assets.Add(node.NodeId, handle);
                    m_order.Add(handle);
                }
                handle.MarkRegistered();
            }
            catch
            {
                Release(handle);
                throw;
            }
            finally
            {
                lock (m_lock)
                {
                    m_pending.Remove(node.NodeId);
                }
            }

            try
            {
                if (handle.ConfigurableAssetIdBinding != null)
                {
                    handle.ConfigurableAssetIdBinding.OnChangedAsync = (assetId, ct) =>
                        Manager?.OnAssetIdChangedAsync(handle, assetId, ct) ?? default;
                }
                else if (AssetIdentification.FindProperty(
                        handle.Context,
                        node,
                        handle.DiNamespaceIndex,
                        AssetIdentification.AssetId) is BaseVariableState own &&
                    (own.AccessLevel & AccessLevels.CurrentWrite) == 0)
                {
                    // §7 asks for an AssetId end users can write; this one keeps
                    // the asset out of "AMB Configurable Asset Identification".
                    Logger.AssetIdNotWritable(handle.BrowseName);
                }

                // The aliases follow the values the application or a client
                // writes to the identification later (§8.2.3).
                handle.WatchIdentification(changed => Manager?.OnIdentificationChanged(changed));
                Logger.AssetRegistered(handle.BrowseName, handle.NodeId, handle.ProductInstanceUri);
                await manager.OnAssetRegisteredAsync(handle, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The caller gets no handle to use or unregister the asset
                // with, so the registration is undone - aliases included - and
                // a retry starts afresh instead of failing with BadNodeIdExists.
                await RollBackAsync(handle).ConfigureAwait(false);
                throw;
            }
            return handle;
        }

        /// <inheritdoc/>
        public bool TryGetAsset(NodeId nodeId, [NotNullWhen(true)] out IAssetHandle? asset)
        {
            lock (m_lock)
            {
                if (m_assets.TryGetValue(nodeId, out AssetHandle? handle))
                {
                    asset = handle;
                    return true;
                }
            }
            asset = null;
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> DefineLocationAsync(
            AssetLocationKind kind,
            string path,
            CancellationToken cancellationToken = default)
        {
            AmbNodeManager manager = Manager ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Asset Management Basics node manager is not part of the server. " +
                    "Register it with AddAssetManagement() or construct an AmbNodeManager with this registry.");
            BaseObjectState location = await manager.EnsureLocationAsync(kind, path, cancellationToken)
                .ConfigureAwait(false);
            return location.NodeId;
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> DefineDictionaryEntryAsync(
            string irdi,
            LocalizedText displayName = default,
            CancellationToken cancellationToken = default)
        {
            AmbNodeManager manager = Manager ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Asset Management Basics node manager is not part of the server. " +
                    "Register it with AddAssetManagement() or construct an AmbNodeManager with this registry.");
            return manager.EnsureDictionaryEntryAsync(irdi, displayName, cancellationToken);
        }

        /// <summary>
        /// Gets the registered assets.
        /// </summary>
        internal ArrayOf<AssetHandle> Snapshot()
        {
            lock (m_lock)
            {
                return m_order.ToArray().ToArrayOf();
            }
        }

        /// <summary>
        /// Connects the node manager the registry serves.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Another node manager is already connected.
        /// </exception>
        internal void Attach(AmbNodeManager manager)
        {
            lock (m_lock)
            {
                if (m_manager != null && !ReferenceEquals(m_manager, manager))
                {
                    throw new InvalidOperationException(
                        "The asset management already serves another Asset Management Basics node manager. " +
                        "A server has one.");
                }
                m_manager = manager;
                m_logger = manager.Server.Telemetry.CreateLogger<AssetManagement>();
            }
        }

        /// <summary>
        /// Disconnects the node manager the registry serves.
        /// </summary>
        internal void Detach(AmbNodeManager manager)
        {
            lock (m_lock)
            {
                if (ReferenceEquals(m_manager, manager))
                {
                    m_manager = null;
                }
            }
        }

        /// <summary>
        /// Removes an asset from the registry.
        /// </summary>
        /// <remarks>
        /// Cancellation is honored before the asset is touched; once removed
        /// from the registry, its aliases and locations are withdrawn to the
        /// end, since a second call finds nothing left to unregister.
        /// </remarks>
        internal async ValueTask UnregisterAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!handle.MarkUnregistered())
            {
                return;
            }

            AmbNodeManager? manager;
            lock (m_lock)
            {
                m_assets.Remove(handle.NodeId);
                m_order.Remove(handle);
                manager = m_manager;
            }
            handle.UnwatchIdentification();
            Release(handle);
            Logger.AssetUnregistered(handle.BrowseName, handle.NodeId);

            if (manager != null)
            {
                await manager.OnAssetUnregisteredAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Undoes a registration that failed after it was committed.
        /// </summary>
        private async ValueTask RollBackAsync(AssetHandle handle)
        {
            try
            {
                await UnregisterAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Logger.RegistrationNotRolledBack(ex, handle.BrowseName);
            }
        }

        /// <summary>
        /// Gives back what binding the asset took: the write handlers of a
        /// configurable <c>AssetId</c> and the documentation links.
        /// </summary>
        private static void Release(AssetHandle handle)
        {
            handle.ConfigurableAssetIdBinding?.Release();
            handle.ConfigurableAssetIdBinding?.Dispose();
            handle.DocumentationLinks?.Dispose();
        }

        private ILogger Logger
        {
            get
            {
                lock (m_lock)
                {
                    return m_logger;
                }
            }
        }

        private readonly Lock m_lock = new();
        private readonly HashSet<NodeId> m_pending = [];
        private readonly Dictionary<NodeId, AssetHandle> m_assets = [];
        private readonly List<AssetHandle> m_order = [];
        private AmbNodeManager? m_manager;
        private ILogger m_logger = NullLogger.Instance;
    }

    internal static partial class AssetManagementLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.AssetManagement + 0,
            Level = LogLevel.Information,
            Message = "Asset {BrowseName} ({NodeId}) registered with ProductInstanceUri '{ProductInstanceUri}'.")]
        public static partial void AssetRegistered(
            this ILogger logger,
            QualifiedName browseName,
            NodeId nodeId,
            string productInstanceUri);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetManagement + 1,
            Level = LogLevel.Information,
            Message = "Asset {BrowseName} ({NodeId}) unregistered.")]
        public static partial void AssetUnregistered(this ILogger logger, QualifiedName browseName, NodeId nodeId);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetManagement + 2,
            Level = LogLevel.Warning,
            Message = "Asset {BrowseName} publishes an AssetId clients cannot write; OPC 10000-110 §7 asks for a " +
                "writable one. Register the asset WithConfigurableAssetId() to make it writable and persistent.")]
        public static partial void AssetIdNotWritable(this ILogger logger, QualifiedName browseName);

        [LoggerMessage(
            EventId = AmbServerEventIds.AssetManagement + 3,
            Level = LogLevel.Error,
            Message = "The failed registration of asset {BrowseName} could not be undone.")]
        public static partial void RegistrationNotRolledBack(
            this ILogger logger,
            Exception exception,
            QualifiedName browseName);
    }
}
