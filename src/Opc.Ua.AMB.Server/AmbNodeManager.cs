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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.AMB.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Node manager for OPC 10000-110 Asset Management Basics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The manager is a sidecar: it loads the AMB model - the interfaces,
    /// condition classes and reference types, the <c>Assets</c> alias
    /// categories below <c>0:Aliases</c> and the location entry points below
    /// <c>0:Locations</c> - and serves the assets that other node managers
    /// own and register with its <see cref="AssetManagement"/>. It claims no
    /// Device Integration ownership, so it composes with <c>AddOpcUaDi</c>
    /// and every companion specification built on DI.
    /// </para>
    /// <para>
    /// The registered assets are listed in the alias categories
    /// <c>AssetsByProductInstanceUri</c> and - those with an <c>AssetId</c> -
    /// <c>AssetsByAssetId</c> (§8.2), with one <c>AliasNameType</c> object per
    /// alias name, and the <c>FindAlias</c> methods of the three categories
    /// answer from them.
    /// </para>
    /// <para>
    /// The health alarms and maintenance conditions of the assets are
    /// instances of server-specific subtypes of the Device Integration alarm
    /// types, which the manager creates in its type namespace and which
    /// implement <c>IRootCauseIndicationType</c> (§9.4.2) and
    /// <c>IMaintenanceEventType</c> (§12.2).
    /// </para>
    /// <para>
    /// The conformance units and the server facet are evaluated from the
    /// registered assets whenever they are read. The server aggregates them
    /// once its node managers are up, so assets registered during startup
    /// count; a change afterwards is published again, which adds what the
    /// assets meet now and withdraws what they no longer meet.
    /// </para>
    /// </remarks>
    public sealed partial class AmbNodeManager : FluentNodeManagerBase, IConformanceContributor
    {
        /// <summary>
        /// Creates the node manager without hosting integration.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="assetManagement">The registry the manager serves.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="assetManagement"/> already serves another manager.
        /// </exception>
        public AmbNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            AssetManagement assetManagement)
            : this(server, configuration, assetManagement, setupRunner: null)
        {
        }

        internal AmbNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            AssetManagement assetManagement,
            AmbSetupRunner? setupRunner)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<AmbNodeManager>(),
                NamespaceUrisOf(assetManagement))
        {
            m_assetManagement = assetManagement;
            m_setupRunner = setupRunner;
            m_ambLogger = server.Telemetry.CreateLogger<AmbNodeManager>();
            RegisterEncodeables(server.Factory);

            // Counter identifiers: alias objects come and go with the assets
            // and their AssetIds, so browse paths repeat over time.
            NodeIdFactory = NodeIdFactory.WithMode(NodeIdAssignmentMode.Counter);
            assetManagement.Attach(this);
        }

        /// <summary>
        /// Gets the registry of the manageable assets.
        /// </summary>
        public IAssetManagement Assets => m_assetManagement;

        /// <summary>
        /// Gets the index of the application-owned instance namespace.
        /// </summary>
        public ushort InstanceNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(m_assetManagement.Options.InstanceNamespaceUri);

        /// <summary>
        /// Gets the index of the AMB model namespace.
        /// </summary>
        public ushort AmbNamespaceIndex => (ushort)Server.NamespaceUris.GetIndex(Namespaces.AMB);

        /// <inheritdoc/>
        public ArrayOf<QualifiedName> ConformanceUnits => AmbConformance.EvaluateUnits(
            m_assetManagement.Snapshot(),
            m_assetManagement.Store.IsPersistent,
            UseServerDefinedAlarmTypes,
            Server.TypeTree,
            IsDictionaryEntry);

        /// <inheritdoc/>
        public ArrayOf<string> ServerProfiles => AmbConformance.EvaluateProfiles(ConformanceUnits);

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUaAMB(context));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Wires the asset discovery and lists the assets registered so far,
        /// then runs the configurators registered with
        /// <c>ConfigureAssetManagement</c>, while the builder is still open.
        /// </remarks>
        protected override async ValueTask ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            await InitializeAlarmTypesAsync(cancellationToken).ConfigureAwait(false);
            await InitializeDiscoveryAsync(cancellationToken).ConfigureAwait(false);
            await InitializeLocationsAsync(cancellationToken).ConfigureAwait(false);
            if (m_setupRunner != null)
            {
                await m_setupRunner.RunAsync(this, builder, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Called by the registry after an asset was registered or removed.
        /// </summary>
        /// <remarks>
        /// During startup the server has not aggregated the conformance units
        /// yet and picks the change up when it does. Afterwards the units are
        /// published again; the server reads every contributor anew, so a
        /// unit the assets no longer meet is withdrawn.
        /// </remarks>
        internal async ValueTask OnAssetsChangedAsync(CancellationToken cancellationToken)
        {
            if (Server is ServerInternalData { ConformanceUnitsManager: { } conformance })
            {
                conformance.Register(this);
                await conformance.PublishAsync(cancellationToken).ConfigureAwait(false);
                m_ambLogger.ConformanceRepublished();
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_assetManagement.Detach(this);
                m_aliasIndex?.Dispose();
                m_aliasLock.Dispose();
                m_typesLock.Dispose();
                m_locationsLock.Dispose();
            }
            base.Dispose(disposing);
        }

        private static string[] NamespaceUrisOf(AssetManagement assetManagement)
        {
            if (assetManagement == null)
            {
                throw new ArgumentNullException(nameof(assetManagement));
            }
            return assetManagement.Options.GetNamespaceUris();
        }

        private static void RegisterEncodeables(IEncodeableFactory factory)
        {
            if (factory.ContainsEncodeableType(DataTypeIds.RootCauseDataType))
            {
                return;
            }
            factory.Builder.AddOpcUaAMB().Commit();
        }

        private readonly AssetManagement m_assetManagement;
        private readonly AmbSetupRunner? m_setupRunner;
        private readonly ILogger m_ambLogger;
    }

    internal static partial class AmbNodeManagerLog
    {
        [LoggerMessage(
            EventId = AmbServerEventIds.AmbNodeManager + 0,
            Level = LogLevel.Debug,
            Message = "AMB conformance units and server profiles published again after an asset change.")]
        public static partial void ConformanceRepublished(this ILogger logger);

        [LoggerMessage(
            EventId = AmbServerEventIds.AmbNodeManager + 1,
            Level = LogLevel.Information,
            Message = "Asset {BrowseName} is listed as '{AliasName}' in AssetsByAssetId now.")]
        public static partial void AssetIdAliasMoved(this ILogger logger, QualifiedName browseName, string aliasName);

        [LoggerMessage(
            EventId = AmbServerEventIds.AmbNodeManager + 2,
            Level = LogLevel.Warning,
            Message = "The aliases of asset {BrowseName} could not follow a change of its identification.")]
        public static partial void IdentificationNotFollowed(
            this ILogger logger,
            Exception exception,
            QualifiedName browseName);
    }
}
