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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;

namespace SiteComposition
{
    /// <summary>
    /// Runtime options for the site composition sample.
    /// </summary>
    public sealed class SiteCompositionOptions
    {
        /// <summary>
        /// Gets or sets the endpoint of the server that owns the pumps.
        /// </summary>
        public string? PumpServerEndpointUrl { get; set; }

        /// <summary>
        /// Gets or sets the endpoint of the server that owns the generator sets.
        /// </summary>
        public string? GeneratorServerEndpointUrl { get; set; }
    }

    /// <summary>
    /// A supervisory node manager that owns no devices and composes the machines of
    /// other servers into a single scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the SCADA-level counterpart to a device server. It publishes a site
    /// stage - ground, buildings, lighting and a control-room camera - and declares
    /// one cross-server component binding per subordinate server. A connector run
    /// with federation enabled opens a session to each of those servers, discovers
    /// their representations and drives their bindings into the same stage, so one
    /// scene shows machines owned by three different servers, live.
    /// </para>
    /// <para>
    /// Nothing is mirrored. The site server never proxies a subordinate's address
    /// space; it only says where the machines are and lets the connector talk to
    /// each owner directly, so there is no cache to invalidate and no second copy of
    /// the truth.
    /// </para>
    /// </remarks>
    public sealed partial class SiteNodeManager : FluentNodeManagerBase
    {
        private const string SiteRootLayerIdentifier = "asset-repo/Site.usd";
        private const string SitePrimPath = "/Site";
        private const string PumpHallPrimPath = "/Site/PumpHall";
        private const string PowerhousePrimPath = "/Site/Powerhouse";

        private static readonly Guid s_pumpComponentId =
            new("b41d7e20-0001-4f52-9a63-1c7d8e0b5a01");

        private static readonly Guid s_generatorComponentId =
            new("b41d7e20-0002-4f52-9a63-1c7d8e0b5a02");

        private readonly SiteCompositionOptions m_options;
        private readonly ILogger m_log;
        private OpenUsdRootState? m_openUsdRoot;
        private OpenUsdStageState? m_siteStage;
        private FolderState? m_areas;

        /// <summary>
        /// Initialises a new <see cref="SiteNodeManager"/>.
        /// </summary>
        /// <param name="server">The server the manager belongs to.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">Sample options naming the subordinate servers.</param>
        public SiteNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IOptions<SiteCompositionOptions>? options = null)
            : base(server, configuration, Namespaces.Site, Opc.Ua.OpenUsd.Namespaces.OpenUSD)
        {
            SystemContext.NodeIdFactory = this;
            m_options = options?.Value ?? new SiteCompositionOptions();
            m_log = server.Telemetry.CreateLogger<SiteNodeManager>();
        }

        /// <summary>
        /// Gets the namespace index this sample's instances live in.
        /// </summary>
        private ushort SiteNamespaceIndex => NamespaceIndexes[0];

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)        {
            if (node is BaseInstanceState { Parent: not null } instance)
            {
                return new NodeId(
                    $"{instance.Parent.NodeId.IdentifierAsString}_{instance.SymbolicName}",
                    SiteNamespaceIndex);
            }
            return node.NodeId;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaOpenUsd(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            await MaterialiseSiteTopologyAsync(cancellationToken).ConfigureAwait(false);
            await MaterialiseOpenUsdFacilityAsync(cancellationToken).ConfigureAwait(false);
            await MaterialiseCrossServerCompositionAsync(cancellationToken).ConfigureAwait(false);

            LinkOpenUsdRootToServer(externalReferences);
            LinkAreasToObjectsFolder(externalReferences);

            m_log.SiteAddressSpaceReady(
                m_options.PumpServerEndpointUrl ?? "(none)",
                m_options.GeneratorServerEndpointUrl ?? "(none)");
        }

        /// <summary>
        /// Creates a small browsable site hierarchy, so the supervisory server is a
        /// real server rather than a bare stage host.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async ValueTask MaterialiseSiteTopologyAsync(CancellationToken cancellationToken)
        {
            var areas = new FolderState(null)
            {
                SymbolicName = "Site",
                BrowseName = new QualifiedName("Site", SiteNamespaceIndex),
                DisplayName = new LocalizedText("Site"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                NodeId = new NodeId("Site", SiteNamespaceIndex),
                EventNotifier = EventNotifiers.None,
            };

            AddArea(areas, "PumpHall", "Pump Hall", m_options.PumpServerEndpointUrl);
            AddArea(areas, "Powerhouse", "Powerhouse", m_options.GeneratorServerEndpointUrl);

            SystemContext.AssignInstanceChildNodeIds(areas);
            await AddPredefinedNodeAsync(SystemContext, areas, cancellationToken)
                .ConfigureAwait(false);
            m_areas = areas;
        }

        /// <summary>
        /// Adds one area object recording which server owns the machines in it.
        /// </summary>
        /// <param name="parent">The site folder.</param>
        /// <param name="name">Area browse name.</param>
        /// <param name="displayName">Area display name.</param>
        /// <param name="endpointUrl">Endpoint of the server that owns the area.</param>
        private void AddArea(FolderState parent, string name, string displayName, string? endpointUrl)
        {
            var area = new BaseObjectState(parent)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, SiteNamespaceIndex),
                DisplayName = new LocalizedText(displayName),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
            };
            parent.AddChild(area);

            // The generic variable states are abstract in this stack, so the concrete
            // carrier is the non-generic one with a Variant value.
            var source = new BaseDataVariableState(area)
            {
                SymbolicName = "SourceServer",
                BrowseName = new QualifiedName("SourceServer", SiteNamespaceIndex),
                DisplayName = new LocalizedText("SourceServer"),
                TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = Opc.Ua.Types.DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.From(endpointUrl ?? string.Empty),
            };
            area.AddChild(source);        }

        /// <summary>
        /// Declares one cross-server component binding per subordinate server.
        /// </summary>
        /// <remarks>
        /// The binding carries the endpoint of the server that owns the machines.
        /// A connector without federation still composes the placeholder prim and
        /// renders the site shell; with federation it opens a session to each named
        /// server and brings that server's machines in live. Failing closed on the
        /// client rather than the server is deliberate - the site server can
        /// advertise where the machines are without dictating that anyone connect.
        /// </remarks>
        private async ValueTask MaterialiseCrossServerCompositionAsync(
            CancellationToken cancellationToken)
        {
            if (m_siteStage == null || m_areas == null)
            {
                return;
            }
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);

            OpenUsdRepresentationState site = SystemContext.CreateRepresentation(
                m_areas, m_siteStage.NodeId, SitePrimPath, ns);

            AddSubordinate(site, ns, "PumpHall", s_pumpComponentId, PumpHallPrimPath,
                m_options.PumpServerEndpointUrl);
            AddSubordinate(site, ns, "Powerhouse", s_generatorComponentId, PowerhousePrimPath,
                m_options.GeneratorServerEndpointUrl);

            if (m_openUsdRoot?.Representations is FolderState registry)
            {
                site.RegisterInDiscovery(registry);
            }

            // The representation is built after its owner was registered, so it has
            // to register its own subtree - otherwise it browses from nowhere and a
            // connector reports that the server publishes no representation at all.
            SystemContext.AssignInstanceChildNodeIds(site);
            await AddPredefinedNodeAsync(SystemContext, site, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Declares one subordinate server as a component of the site.
        /// </summary>
        /// <param name="site">The site representation.</param>
        /// <param name="ns">OpenUSD namespace index.</param>
        /// <param name="name">Binding name.</param>
        /// <param name="definitionId">Stable binding definition id.</param>
        /// <param name="primPath">Scope the subordinate's machines land in.</param>
        /// <param name="endpointUrl">Endpoint of the owning server.</param>
        private void AddSubordinate(
            OpenUsdRepresentationState site,
            ushort ns,
            string name,
            Guid definitionId,
            string primPath,
            string? endpointUrl)
        {
            if (string.IsNullOrEmpty(endpointUrl))
            {
                return;
            }
            site.AddComponentBinding(
                SystemContext,
                ns,
                name,
                definitionId,
                OpenUsdCardinalityEnum.Many,
                OpenUsdCompositionArcEnum.Reference,
                primPath,
                componentRepresentation: default,
                assetReference: null,
                dynamic: false,
                changeEventSource: default,
                componentServerUri: endpointUrl,
                componentEndpointUrl: endpointUrl);
        }

        /// <summary>
        /// Creates the well-known OpenUSD facility and the site stage.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async ValueTask MaterialiseOpenUsdFacilityAsync(CancellationToken cancellationToken)
        {
            try
            {
                ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);

                OpenUsdRootState root = SystemContext.CreateInstanceOfOpenUsdRootType(
                    null!, new QualifiedName("OpenUSD", ns));
                root.NodeId = new NodeId("OpenUSD", ns);

                FolderState stages = root.Stages ?? root.CreateOrReplaceStages(SystemContext, null!);
                _ = root.Representations ?? root.CreateOrReplaceRepresentations(SystemContext, null!);

                m_siteStage = SystemContext.CreateInstanceOfOpenUsdStageType(
                    stages, new QualifiedName("SiteStage", ns));
                stages.AddChild(m_siteStage);
                m_siteStage.CreateOrReplaceRootLayerIdentifier(SystemContext, null!)
                    .Value = SiteRootLayerIdentifier;

                List<ServedAsset> served = LoadServedAssets();
                byte[] rootLayerBytes = served
                    .Find(a => a.Kind == OpenUsdAssetKindEnum.RootLayer)!.Bytes;
                byte[] digest;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    digest = sha.ComputeHash(rootLayerBytes);
                }
#pragma warning restore CA1850
                m_siteStage.CreateOrReplaceRootLayerDigest(SystemContext, null!)
                    .Value = (ByteString)digest;
                m_siteStage.CreateOrReplaceRootLayerDigestAlgorithm(SystemContext, null!)
                    .Value = OpenUsdDigestAlgorithmEnum.Sha256;

                root.AddReference(ReferenceTypeIds.HasComponent, true, Opc.Ua.ObjectIds.Server);
                UsdAssetDelivery.AttachStageAssets(SystemContext, m_siteStage, ns, served);

                SystemContext.AssignInstanceChildNodeIds(root);
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken)
                    .ConfigureAwait(false);
                m_openUsdRoot = root;
            }
            catch (Exception ex)
            {
                m_siteStage = null;
                m_openUsdRoot = null;
                m_log.OpenUsdFacilityFailed(ex);
            }
        }

        /// <summary>
        /// Loads the site shell layer this server serves.
        /// </summary>
        /// <returns>The served asset closure.</returns>
        /// <remarks>
        /// Only the site shell. The machine geometry belongs to the subordinate
        /// servers, which serve their own component assets to the same connector.
        /// </remarks>
        private static List<ServedAsset> LoadServedAssets()
        {
            return
            [
                new ServedAsset("Site.usda", OpenUsdAssetKindEnum.RootLayer, ReadEmbeddedAsset("Site.usda")),
            ];
        }

        /// <summary>
        /// Reads one embedded USD layer.
        /// </summary>
        /// <param name="resourceName">Logical resource name.</param>
        /// <returns>The layer bytes, or an empty array when absent.</returns>
        private static byte[] ReadEmbeddedAsset(string resourceName)
        {
            using Stream? s = typeof(SiteNodeManager).Assembly.GetManifestResourceStream(resourceName);
            if (s == null)
            {
                return [];
            }
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Makes the OpenUSD facility browsable from the Server Object.
        /// </summary>
        /// <param name="externalReferences">The shared reference dictionary.</param>
        private void LinkOpenUsdRootToServer(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (m_openUsdRoot == null)
            {
                return;
            }
            if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.Server, out IList<IReference>? refs))
            {
                refs = new List<IReference>();
                externalReferences[Opc.Ua.ObjectIds.Server] = refs;
            }
            refs.Add(new NodeStateReference(
                ReferenceTypeIds.HasComponent, false, m_openUsdRoot.NodeId));
        }

        /// <summary>
        /// Organises the site folder under Objects so a client can browse to it.
        /// </summary>
        /// <param name="externalReferences">The shared reference dictionary.</param>
        private void LinkAreasToObjectsFolder(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (m_areas == null)
            {
                return;
            }
            if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.ObjectsFolder, out IList<IReference>? refs))
            {
                refs = new List<IReference>();
                externalReferences[Opc.Ua.ObjectIds.ObjectsFolder] = refs;
            }
            refs.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, m_areas.NodeId));
            m_areas.AddReference(ReferenceTypeIds.Organizes, true, Opc.Ua.ObjectIds.ObjectsFolder);
        }
    }

    /// <summary>
    /// Namespaces this sample owns.
    /// </summary>
    internal static class Namespaces
    {
        /// <summary>
        /// The site composition sample instance namespace.
        /// </summary>
        public const string Site = "http://opcfoundation.org/UA/Samples/SiteComposition/";
    }

    /// <summary>
    /// Creates the <see cref="SiteNodeManager"/> for the DI hosting pipeline.
    /// </summary>
    public sealed class SiteNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IOptions<SiteCompositionOptions>? m_options;

        /// <summary>
        /// Initialises a factory without options.
        /// </summary>
        public SiteNodeManagerFactory()
        {
        }

        /// <summary>
        /// Initialises a factory with sample options.
        /// </summary>
        /// <param name="options">Sample options.</param>
        public SiteNodeManagerFactory(IOptions<SiteCompositionOptions>? options)
        {
            m_options = options;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Namespaces.Site,
            Opc.Ua.OpenUsd.Namespaces.OpenUSD,
        }.ToArrayOf();

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to server
            IAsyncNodeManager nodeManager = new SiteNodeManager(server, configuration, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }
    }

    /// <summary>
    /// Event id offsets for the site composition server.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative
    /// to the offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>,
    /// so two files cannot silently claim the same id.
    /// </remarks>
    internal static class SiteCompositionServerEventIds
    {
        /// <summary>
        /// Offset for messages raised by <see cref="SiteNodeManager"/>.
        /// </summary>
        public const int SiteNodeManager = 0;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="SiteNodeManager"/>.
    /// </summary>
    internal static partial class SiteNodeManagerLog
    {
        [LoggerMessage(
            EventId = SiteCompositionServerEventIds.SiteNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Site composition ready. Pump server: {PumpServer}. Generator server: {GeneratorServer}.")]
        public static partial void SiteAddressSpaceReady(
            this ILogger logger, string pumpServer, string generatorServer);

        [LoggerMessage(
            EventId = SiteCompositionServerEventIds.SiteNodeManager + 1,
            Level = LogLevel.Error,
            Message = "Failed to materialise the OpenUSD facility.")]
        public static partial void OpenUsdFacilityFailed(this ILogger logger, Exception exception);
    }
}
