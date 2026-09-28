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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.PackML;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Device Integration node manager for the OPC 40200 Weighing Technology
    /// (Scales) model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ScaleDeviceType</c> and <c>ScaleSystemType</c> derive from the DI
    /// <c>ComponentType</c>, so a scale <em>is</em> a Device Integration
    /// component: it belongs in the <c>DeviceSet</c>, and the manager derives
    /// from <see cref="DiNodeManager"/> so the DI hosting pipeline -
    /// <c>ConfigureDevicesFor&lt;ScalesNodeManager&gt;</c> - works on it
    /// unchanged. Every top-level scale and scale system is also organized
    /// into the OPC 40001-1 <c>Machines</c> folder, which the Machinery
    /// Machine Identification facet every OPC 40200 facet requires expects.
    /// </para>
    /// <para>
    /// Model load order: DI, then IA (Machinery's stacklight is an IA type),
    /// then Machinery, then PackML (a scale's <c>State</c> is a PackML state
    /// machine), then Scales.
    /// </para>
    /// </remarks>
    public sealed class ScalesNodeManager : DiNodeManager
    {
        /// <summary>
        /// Creates a Scales node manager with default options and no
        /// DI-hosting integration.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        public ScalesNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Creates a Scales node manager with explicit options.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The manager options.</param>
        public ScalesNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ScalesServerOptions options)
            : this(
                  server,
                  configuration,
                  postSetupRunner: null,
                  new OptionsWrapper<ScalesServerOptions>(
                      options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>
        /// Creates a Scales node manager that participates in the Device
        /// Integration hosting post-setup pipeline.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="postSetupRunner">The DI post-setup runner.</param>
        /// <param name="options">The manager options.</param>
        public ScalesNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<ScalesServerOptions>? options = null)
            : base(server, configuration, postSetupRunner, ManagerNamespaceUris(options?.Value))
        {
            m_options = (options?.Value ?? new ScalesServerOptions()).Validate();

            // The base class logs under the DiNodeManager category; what the
            // Scales runtime logs belongs under its own.
            m_scalesLogger = server.Telemetry.CreateLogger<ScalesNodeManager>();
        }

        /// <summary>
        /// Gets the options the manager was created with.
        /// </summary>
        public ScalesServerOptions Options => m_options;

        /// <summary>
        /// Gets the scales this manager materialised, including those of scale systems.
        /// </summary>
        public ArrayOf<ScaleHandle> Scales
        {
            get
            {
                lock (m_scaleLock)
                {
                    return m_scales;
                }
            }
        }

        /// <summary>
        /// Gets the scale systems this manager materialised.
        /// </summary>
        public ArrayOf<ScaleSystemHandle> ScaleSystems
        {
            get
            {
                lock (m_scaleLock)
                {
                    return m_systems;
                }
            }
        }

        /// <summary>
        /// Gets the DI lock service products are locked with.
        /// </summary>
        public ILockService LockService => m_lockService;

        /// <summary>
        /// Gets the namespace indices a scale's children live in.
        /// </summary>
        public ScaleNamespaceIndices NamespaceIndices =>
            m_namespaces ??= ScaleNamespaceIndices.Resolve(Server.NamespaceUris);

        /// <summary>
        /// Creates a scale below the Device Integration <c>DeviceSet</c>.
        /// </summary>
        /// <param name="browseName">The scale's browse name.</param>
        /// <param name="kind">The kind of scale.</param>
        /// <param name="configure">
        /// Shapes the scale; <c>WithIdentification</c> and at least one
        /// <c>WithWeighingRange</c> are required.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The runtime of the published scale.</returns>
        public ValueTask<ScaleHandle> CreateScaleAsync(
            QualifiedName browseName,
            ScaleKind kind,
            Action<IScaleBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            return CreateScaleAsync(browseName, kind, configure, parent: null, cancellationToken);
        }

        /// <summary>
        /// Creates a scale below an explicit parent.
        /// </summary>
        /// <param name="browseName">The scale's browse name.</param>
        /// <param name="kind">The kind of scale.</param>
        /// <param name="configure">Shapes the scale.</param>
        /// <param name="parent">The parent, or null for the <c>DeviceSet</c>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ScaleHandle> CreateScaleAsync(
            QualifiedName browseName,
            ScaleKind kind,
            Action<IScaleBuilder> configure,
            NodeState? parent,
            CancellationToken cancellationToken = default)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ScaleRuntimeServices services = Services;
            (NodeState effectiveParent, bool isDeviceSet) = ResolveParent(browseName, parent);

            ScaleDeviceState scale = ScaleNodes.CreateScale(SystemContext, kind, effectiveParent, browseName);
            scale.DisplayName = new LocalizedText(browseName.Name);
            scale.ReferenceTypeId = isDeviceSet
                ? Opc.Ua.Types.ReferenceTypeIds.Organizes
                : Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            var builder = new ScaleBuilder(services, scale, kind);
            configure(builder);
            builder.Finish();

            await PublishAsync(effectiveParent, scale, cancellationToken).ConfigureAwait(false);
            ScaleHandle handle = builder.CreateHandle();
            lock (m_scaleLock)
            {
                m_scales = m_scales.AddItem(handle).AddItems(handle.WeighingModules);
            }
            m_scalesLogger.ScaleMaterialised(browseName.Name, kind, scale.NodeId);
            return handle;
        }

        /// <summary>
        /// Creates a scale system below the Device Integration
        /// <c>DeviceSet</c>.
        /// </summary>
        /// <param name="browseName">The system's browse name.</param>
        /// <param name="configure">
        /// Shapes the system and adds its scales; <c>WithIdentification</c>
        /// is required.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The runtime of the published system.</returns>
        public async ValueTask<ScaleSystemHandle> CreateScaleSystemAsync(
            QualifiedName browseName,
            Action<IScaleSystemBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ScaleRuntimeServices services = Services;
            (NodeState parent, _) = ResolveParent(browseName, null);

            ScaleSystemState system = SystemContext.CreateInstanceOfScaleSystemType(parent, browseName);
            system.DisplayName = new LocalizedText(browseName.Name);
            system.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes;
            var builder = new ScaleSystemBuilder(services, system);
            configure(builder);
            builder.Finish();

            await PublishAsync(parent, system, cancellationToken).ConfigureAwait(false);
            ScaleSystemHandle handle = builder.CreateHandle();
            lock (m_scaleLock)
            {
                m_systems = m_systems.AddItem(handle);
                foreach (ScaleHandle scale in handle.Scales)
                {
                    m_scales = m_scales.AddItem(scale).AddItems(scale.WeighingModules);
                }
            }
            m_scalesLogger.ScaleSystemMaterialised(browseName.Name, system.NodeId, handle.Scales.Count);
            return handle;
        }

        /// <summary>
        /// Finds the runtime of a scale by NodeId.
        /// </summary>
        /// <param name="nodeId">The scale's NodeId.</param>
        public ScaleHandle? FindScale(NodeId nodeId)
        {
            lock (m_scaleLock)
            {
                return m_scales.Find(s => s.NodeId == nodeId);
            }
        }

        /// <inheritdoc/>
        public override ArrayOf<string> ServerProfiles
        {
            get
            {
                var profiles = new List<string>(base.ServerProfiles.ToList());
                lock (m_scaleLock)
                {
                    if (m_scales.Count > 0)
                    {
                        profiles.Add(ScalesProfiles.BaseScale);
                    }
                    foreach (ScaleHandle scale in m_scales)
                    {
                        AddDistinct(profiles, ScalesProfiles.FacetOf(scale.Kind));
                        if (scale.ProductionPreset is { } preset)
                        {
                            AddDistinct(
                                profiles,
                                preset.Preset.AddProduct != null && preset.Preset.SelectProduct != null
                                    ? ScalesProfiles.FullProductionPreset
                                    : ScalesProfiles.MinimalProductionPreset);
                        }
                        foreach (ScaleModuleHandle module in scale.Modules)
                        {
                            AddDistinct(
                                profiles,
                                module.Feeder != null ? ScalesProfiles.FeederModule : ScalesProfiles.PrinterModule);
                        }
                    }
                    if (m_systems.Count > 0)
                    {
                        profiles.Add(ScalesProfiles.ScaleSystem);
                    }
                }
                if (m_options.RequireSiUnits)
                {
                    profiles.Add(ScalesProfiles.InternationalSystemOfUnits);
                }
                return profiles.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public override ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                var units = new List<QualifiedName>(base.ConformanceUnits.ToList());
                ushort ns = NamespaceIndices.Scales;
                void Add(string name)
                {
                    var unit = new QualifiedName(name, ns);
                    if (!units.Contains(unit))
                    {
                        units.Add(unit);
                    }
                }
                lock (m_scaleLock)
                {
                    foreach (ScaleHandle scale in m_scales)
                    {
                        Add("Scales ScaleDeviceType");
                        Add(ConformanceUnitOf(scale.Kind));
                        if (scale.ProductionPreset is { } preset)
                        {
                            Add("Scales ProductType");
                            Add(preset.Preset.AddProduct != null
                                ? "Scales DynamicProductAddressSpace"
                                : "Scales StaticProductAddressSpace");
                            if (preset.Preset.SelectProduct != null)
                            {
                                Add("Scales SelectProduct");
                            }
                            if (preset.Preset.AddProduct != null)
                            {
                                Add("Scales ManageProduct");
                            }
                        }
                        if (scale.Recipes?.Scale.Recipes != null)
                        {
                            Add("Scales RecipeManagment");
                            Add("Scales DynamicRecipeManagement");
                            if (scale.Recipes.RecipeFiles)
                            {
                                Add("Scales FileRecipeManagement");
                            }
                        }
                        foreach (ScaleModuleHandle module in scale.Modules)
                        {
                            Add(module.Feeder != null ? "Scales FeederModule" : "Scales PrinterModule");
                        }
                    }
                    if (m_systems.Count > 0)
                    {
                        Add("Scales ScaleSystemType");
                    }
                    if (m_scales.Count > 0 || m_systems.Count > 0)
                    {
                        Add("Scales DataChange");
                    }
                }
                if (m_options.RequireSiUnits)
                {
                    Add("Scales Display_SI_units");
                    Add("Scales InputArgument_SI_units");
                }
                return units.ToArrayOf();
            }
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaPackML(context);
            nodes.AddOpcUaScales(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_lockService.Dispose();
            }
            base.Dispose(disposing);
        }

        private ScaleRuntimeServices Services
        {
            get
            {
                if (m_services != null)
                {
                    return m_services;
                }
                if (!m_lockAttached && Server.SessionManager != null)
                {
                    m_lockService.AttachToSessionManager(Server.SessionManager);
                    m_lockAttached = true;
                }
                return m_services = new ScaleRuntimeServices(
                    SystemContext,
                    NamespaceIndices,
                    m_options,
                    AddPredefinedNodeSynchronously,
                    (nodeId, ct) => DeleteNodeAsync(SystemContext, nodeId, ct),
                    (type, baseType) => Server.TypeTree.IsTypeOf(type, baseType),
                    m_lockService,
                    m_scalesLogger);
            }
        }

        private async ValueTask PublishAsync(NodeState parent, BaseObjectState node, CancellationToken cancellationToken)
        {
            parent.AddChild(node);

            // A scale reports scale events and alarms, so it has to be a
            // notifier before a client can subscribe to it.
            node.EventNotifier |= EventNotifiers.SubscribeToEvents;
            WireNestedNotifiers(node, node);
            await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
            await AddRootNotifierAsync(node, cancellationToken).ConfigureAwait(false);

            if (m_options.OrganizeIntoMachinesFolder && !TryAddToMachinesFolder(node))
            {
                m_scalesLogger.MachinesFolderUnavailable(node.BrowseName.Name);
            }
        }

        /// <summary>
        /// Makes every nested event source (the scales of a scale system,
        /// weighing modules, feeder and printer modules) a notifier of its
        /// own and links it to its nearest notifier ancestor with
        /// <c>HasNotifier</c>, so a client can both subscribe to it directly
        /// and discover the notifier hierarchy by browsing. Events still
        /// reach the ancestors through the parent chain.
        /// </summary>
        private void WireNestedNotifiers(NodeState node, BaseObjectState notifier)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                BaseObjectState next = notifier;
                if (child is ScaleDeviceState or FeederModuleState or PrinterModuleState)
                {
                    var source = (BaseObjectState)child;
                    source.EventNotifier |= EventNotifiers.SubscribeToEvents;
                    if (!notifier.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasNotifier, false, source.NodeId))
                    {
                        notifier.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, false, source.NodeId);
                        source.AddReference(Opc.Ua.ReferenceTypeIds.HasNotifier, true, notifier.NodeId);
                    }
                    next = source;
                }
                if (child is BaseObjectState)
                {
                    WireNestedNotifiers(child, next);
                }
            }
        }

        private (NodeState Parent, bool IsDeviceSet) ResolveParent(QualifiedName browseName, NodeState? parent)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw new ArgumentException("A browse name is required.", nameof(browseName));
            }
            NodeState effective = parent ?? FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris))
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Device Integration DeviceSet is not available.");

            // The duplicate check is by browse name rather than by predicting
            // the NodeId the factory will mint.
            var existing = new List<BaseInstanceState>();
            effective.GetChildren(SystemContext, existing);
            if (existing.Any(child => child.BrowseName == browseName))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "'{0}' already contains a child named '{1}'.",
                    effective.BrowseName,
                    browseName);
            }
            return (effective, parent == null);
        }

        private static string ConformanceUnitOf(ScaleKind kind)
        {
            return kind switch
            {
                ScaleKind.Simple => "Scales SimpleScale",
                ScaleKind.Laboratory => "Scales LaboratoryScale",
                ScaleKind.Hopper => "Scales HopperScale",
                ScaleKind.WeighingModule => "Scales WeighingBridge",
                ScaleKind.AutomaticFilling => "Scales AutomaticFillingScale",
                ScaleKind.Catchweigher => "Scales Catchweigher",
                ScaleKind.Checkweigher => "Scales Checkweigher",
                ScaleKind.AutomaticWeightPriceLabeler => "Scales AutomaticWeightPriceLabeler",
                ScaleKind.Continuous => "Scales ContinuousScale",
                ScaleKind.LossInWeight => "Scales Loss In Weight Scale",
                ScaleKind.PieceCounting => "Scales PieceCountingScale",
                ScaleKind.Recipe => "Scales RecipeScale",
                ScaleKind.TotalizingHopper => "Scales TotalizingHopperScale",
                ScaleKind.Vehicle => "Scales VehicleScale",
                _ => "Scales ScaleDeviceType"
            };
        }

        private static void AddDistinct(List<string> list, string value)
        {
            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }

        /// <summary>
        /// Builds the namespace list the base manager registers. DI is always
        /// appended by the base, so it is not repeated here.
        /// </summary>
        private static string[] ManagerNamespaceUris(ScalesServerOptions? options)
        {
            options ??= new ScalesServerOptions();
            var uris = new List<string>(4 + options.AdditionalNamespaceUris.Length)
            {
                Namespaces.Scales,
                Opc.Ua.PackML.Namespaces.PackML,
                Opc.Ua.Machinery.Namespaces.Machinery,
                Opc.Ua.IA.Namespaces.IA
            };
            uris.AddRange(options.AdditionalNamespaceUris);
            return [.. uris];
        }

        private readonly ScalesServerOptions m_options;
        private readonly ILogger m_scalesLogger;
        private readonly Lock m_scaleLock = new();
        private readonly DefaultLockService m_lockService = new();
        private ArrayOf<ScaleHandle> m_scales = [];
        private ArrayOf<ScaleSystemHandle> m_systems = [];
        private ScaleNamespaceIndices? m_namespaces;
        private ScaleRuntimeServices? m_services;
        private bool m_lockAttached;
    }

    internal static partial class ScalesNodeManagerLog
    {
        [LoggerMessage(
            EventId = ScalesServerEventIds.ScalesNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Materialised scale '{Name}' ({Kind}), NodeId={NodeId}.")]
        public static partial void ScaleMaterialised(this ILogger logger, string? name, ScaleKind kind, NodeId nodeId);

        [LoggerMessage(
            EventId = ScalesServerEventIds.ScalesNodeManager + 1,
            Level = LogLevel.Information,
            Message = "Materialised scale system '{Name}', NodeId={NodeId}, with {Count} scale(s).")]
        public static partial void ScaleSystemMaterialised(this ILogger logger, string? name, NodeId nodeId, int count);

        [LoggerMessage(
            EventId = ScalesServerEventIds.ScalesNodeManager + 2,
            Level = LogLevel.Warning,
            Message = "The Machinery Machines folder is unavailable - '{Name}' is reachable from the DeviceSet only.")]
        public static partial void MachinesFolderUnavailable(this ILogger logger, string? name);
    }
}
