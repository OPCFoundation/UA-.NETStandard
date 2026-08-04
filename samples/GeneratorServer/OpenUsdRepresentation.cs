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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Generators;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;

namespace Generators
{
    /// <summary>
    /// The OpenUSD twin: one independent, live-driven 3D representation per
    /// configured generator set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each set owns a <see cref="GeneratorTwin"/> carrying its own prim path,
    /// its own signal Variables and its own bindings, so <c>--generators N</c>
    /// renders N machines that move independently.
    /// </para>
    /// <para>
    /// One authoring rule dominates this file: a prim driven by a Translation,
    /// Rotation or Scale render target must declare <c>xformOp:transform</c> in its
    /// <c>xformOpOrder</c>, because a connector folds those targets into a single
    /// matrix op and <c>xformOpOrder</c> is <c>uniform</c> - it cannot be rewritten
    /// from the stronger layer the connector edits. Naming <c>xformOp:translate</c>
    /// there instead makes USD discard every value silently, and every set renders
    /// on the origin. The asset generator enforces this; a test pins it.
    /// </para>
    /// </remarks>
    public partial class GeneratorNodeManager
    {
        private const string PowerhouseRootLayerIdentifier = "asset-repo/Powerhouse.usd";
        private const string GeneratorsScopePrimPath = "/Powerhouse/Generators";
        private const string GeneratorAssetReference = "@generator.usda@</Generator>";
        private const double BaySpacingMetres = 6.0;
        private const double SignalSamplingIntervalMilliseconds = 250;

        private readonly ConcurrentDictionary<NodeId, GeneratorTwin> m_twins = new();
        private (BaseVariableState Variable, Func<double> Getter)[] m_liveSignals = [];
        private OpenUsdRootState? m_openUsdRoot;
        private OpenUsdStageState? m_powerhouseStage;

        /// <summary>
        /// Creates the well-known OpenUSD facility and the powerhouse stage.
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

                FolderState stages = root.Stages
                    ?? root.CreateOrReplaceStages(SystemContext, null!);
                _ = root.Representations
                    ?? root.CreateOrReplaceRepresentations(SystemContext, null!);

                m_powerhouseStage = SystemContext.CreateInstanceOfOpenUsdStageType(
                    stages, new QualifiedName("PowerhouseStage", ns));
                stages.AddChild(m_powerhouseStage);
                m_powerhouseStage.CreateOrReplaceRootLayerIdentifier(SystemContext, null!)
                    .Value = PowerhouseRootLayerIdentifier;

                // The digest covers the resolved root-layer content, never the
                // identifier string, so a connector can detect tampering with the
                // bytes it actually composes.
                List<ServedAsset> servedAssets = LoadServedAssets();
                byte[] rootLayerBytes = servedAssets
                    .Find(a => a.Kind == OpenUsdAssetKindEnum.RootLayer)!.Bytes;
                byte[] digest;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    digest = sha.ComputeHash(rootLayerBytes);
                }
#pragma warning restore CA1850
                m_powerhouseStage.CreateOrReplaceRootLayerDigest(SystemContext, null!)
                    .Value = (ByteString)digest;
                m_powerhouseStage.CreateOrReplaceRootLayerDigestAlgorithm(SystemContext, null!)
                    .Value = OpenUsdDigestAlgorithmEnum.Sha256;

                // The facility is a component of the Server Object so any conformant
                // connector can browse Server -> OpenUSD -> Representations. The
                // matching forward reference is added through externalReferences.
                root.AddReference(ReferenceTypeIds.HasComponent, true, Opc.Ua.ObjectIds.Server);

                UsdAssetDelivery.AttachStageAssets(SystemContext, m_powerhouseStage, ns, servedAssets);

                SystemContext.AssignInstanceChildNodeIds(root);
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken)
                    .ConfigureAwait(false);

                m_openUsdRoot = root;
            }
            catch (Exception ex)
            {
                m_powerhouseStage = null;
                m_openUsdRoot = null;
                m_logger.LogError(ex, "Failed to materialise the OpenUSD facility.");
            }
        }

        /// <summary>
        /// Loads the embedded USD layers this server serves.
        /// </summary>
        /// <returns>The served asset closure.</returns>
        private static List<ServedAsset> LoadServedAssets()
        {
            return
            [
                new ServedAsset(
                    "Powerhouse.usda",
                    OpenUsdAssetKindEnum.RootLayer,
                    ReadEmbeddedAsset("Powerhouse.usda")),
                new ServedAsset(
                    "generator.usda",
                    OpenUsdAssetKindEnum.Reference,
                    ReadEmbeddedAsset("generator.usda")),
            ];
        }

        /// <summary>
        /// Reads one embedded USD layer.
        /// </summary>
        /// <param name="resourceName">Logical resource name.</param>
        /// <returns>The layer bytes, or an empty array when absent.</returns>
        private static byte[] ReadEmbeddedAsset(string resourceName)
        {
            using Stream? s = typeof(GeneratorNodeManager).Assembly
                .GetManifestResourceStream(resourceName);
            if (s == null)
            {
                return [];
            }
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            LinkOpenUsdRootToServer(externalReferences);
        }

        /// <summary>
        /// Adds the Server to OpenUSD forward reference through the shared
        /// external-reference dictionary, so the facility is browsable from the
        /// Server Object even though that node belongs to the core node manager.
        /// </summary>
        /// <param name="externalReferences">The shared reference dictionary.</param>
        private void LinkOpenUsdRootToServer(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (m_openUsdRoot == null)
            {
                return;
            }
            if (!externalReferences.TryGetValue(
                Opc.Ua.ObjectIds.Server, out IList<IReference>? references))
            {
                references = new List<IReference>();
                externalReferences[Opc.Ua.ObjectIds.Server] = references;
            }
            references.Add(new NodeStateReference(
                ReferenceTypeIds.HasComponent, false, m_openUsdRoot.NodeId));
        }

        /// <summary>
        /// Attaches one set's representation, its signal Variables and its live
        /// bindings.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="setNumber">One-based unit number, which fixes the bay.</param>
        private void AttachOpenUsdRepresentation(GeneratorSetState set, int setNumber)
        {
            if (m_powerhouseStage == null)
            {
                return;
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            string primPath = PrimPathFor(set);
            var twin = new GeneratorTwin(primPath, setNumber);

            OpenUsdRepresentationState rep = SystemContext.CreateRepresentation(
                set, m_powerhouseStage.NodeId, primPath, ns);

            CreateSignals(set, twin);
            CreateBindings(rep, ns, twin);

            m_twins[set.NodeId] = twin;
            twin.Representation = rep;
        }

        /// <summary>
        /// Returns the prim path a set is composed at.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <returns>The prim path.</returns>
        private static string PrimPathFor(GeneratorSetState set)
        {
            return FormattableString.Invariant($"{GeneratorsScopePrimPath}/{set.BrowseName.Name}");
        }

        /// <summary>
        /// Creates the Variables that carry render-only signals - values the twin
        /// needs that the companion specification does not model.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="twin">The twin to populate.</param>
        private void CreateSignals(GeneratorSetState set, GeneratorTwin twin)
        {
            twin.BayPosition = CreateVariable(
                set,
                "BayPosition",
                Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                Variant.From(new ExtensionObject(new ThreeDCartesianCoordinates
                {
                    X = 0.0,
                    Y = (twin.SetNumber - 1) * BaySpacingMetres,
                    Z = 0.0,
                })));

            twin.FanAngle = CreateVariable(
                set, "CoolingFanAngle", Opc.Ua.DataTypeIds.Double, Variant.From(0.0));
            twin.LoadNeedle = CreateVariable(
                set, "LoadGaugeAngle", Opc.Ua.DataTypeIds.Double, Variant.From(0.0));
            twin.TempNeedle = CreateVariable(
                set, "CoolantGaugeAngle", Opc.Ua.DataTypeIds.Double, Variant.From(0.0));
            twin.ExhaustCelsius = CreateVariable(
                set, "ExhaustSurfaceTemperature", Opc.Ua.DataTypeIds.Double, Variant.From(25.0));
            twin.RadiatorCelsius = CreateVariable(
                set, "RadiatorSurfaceTemperature", Opc.Ua.DataTypeIds.Double, Variant.From(25.0));
            twin.FuelSurface = CreateVariable(
                set,
                "FuelSurfacePosition",
                Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                Variant.From(new ExtensionObject(FuelSurfaceAt(
                    GeneratorDatasheet.Simulation.InitialFuelPercent))));

            twin.ProtectionTripped = CreateVariable(
                set, "ProtectionTripped", Opc.Ua.DataTypeIds.Boolean, Variant.From(false));
            twin.CoolantOverTemperature = CreateVariable(
                set, "CoolantOverTemperature", Opc.Ua.DataTypeIds.Boolean, Variant.From(false));
            twin.LowOilPressure = CreateVariable(
                set, "LowOilPressure", Opc.Ua.DataTypeIds.Boolean, Variant.From(false));
            twin.Running = CreateVariable(
                set, "Running", Opc.Ua.DataTypeIds.Boolean, Variant.From(false));
            twin.OperatingStateName = CreateVariable(
                set, "OperatingStateName", Opc.Ua.DataTypeIds.String, Variant.From("Off"));
        }

        /// <summary>
        /// Returns the fuel surface position for a tank level.
        /// </summary>
        /// <param name="levelPercent">Tank level, in percent.</param>
        /// <returns>The surface position inside the tank.</returns>
        /// <remarks>
        /// The connector turns a scalar into a scalar, and a Translation target
        /// needs a <c>double3</c>, so the surface height is published as a
        /// structured coordinate rather than a bare number.
        /// </remarks>
        private static ThreeDCartesianCoordinates FuelSurfaceAt(double levelPercent)
        {
            const double tankFloor = -0.24;
            const double tankHeight = 0.50;
            return new ThreeDCartesianCoordinates
            {
                X = 0.0,
                Y = 0.0,
                Z = tankFloor + (tankHeight * Numeric.Clamp(levelPercent, 0.0, 100.0) / 100.0),
            };
        }

        /// <summary>
        /// Creates one render-only Variable on a set.
        /// </summary>
        /// <param name="set">Owning generator set.</param>
        /// <param name="name">Browse and symbolic name.</param>
        /// <param name="dataType">Variable data type.</param>
        /// <param name="initialValue">Initial value.</param>
        /// <returns>The created Variable.</returns>
        private BaseDataVariableState CreateVariable(
            GeneratorSetState set, string name, NodeId dataType, Variant initialValue)
        {
            var v = new BaseDataVariableState(set)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, set.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = initialValue,
                MinimumSamplingInterval = SignalSamplingIntervalMilliseconds,
            };
            set.AddChild(v);
            v.NodeId = SystemContext.NodeIdFactory.New(SystemContext, v);
            return v;
        }

        /// <summary>
        /// Registers a set's hand-built signal Variables so monitored items sample
        /// them, and adds its shaft angle to the publish set.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>
        /// Registers the one set passed in, not every set seen so far. This runs
        /// once per set as it is created and the twin is already in the dictionary
        /// by then, so walking the dictionary would re-register every earlier set
        /// and publish the first one N times per tick.
        /// </remarks>
        private async ValueTask RegisterOpenUsdSignalsAsync(
            GeneratorSetState set, CancellationToken cancellationToken)
        {
            if (!m_twins.TryGetValue(set.NodeId, out GeneratorTwin? twin))
            {
                return;
            }
            foreach (BaseDataVariableState? signal in twin.Signals)
            {
                if (signal != null)
                {
                    await AddPredefinedNodeAsync(SystemContext, signal, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Organises every set's representation into the discovery registry, so a
        /// connector finds all of them rather than only the first, and declares the
        /// plant-level aggregation that composes one generator prim per set.
        /// </summary>
        /// <remarks>
        /// The aggregation is deliberately <em>not</em> declared dynamic. The set of
        /// generators is fixed by <c>--generators</c> at start-up, and a dynamic
        /// binding makes a connector's stale-prim sweep deactivate the component
        /// prims that live under the same target prefix.
        /// </remarks>
        private async ValueTask OrganiseRepresentationsAsync(CancellationToken cancellationToken)
        {
            if (m_openUsdRoot?.Representations is FolderState registry)
            {
                foreach (GeneratorTwin twin in m_twins.Values)
                {
                    twin.Representation?.RegisterInDiscovery(registry);
                }
            }

            await MaterialisePlantAggregationAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Declares the DeviceSet-level representation whose single <c>Many</c>
        /// component binding composes one referenced generator per configured set.
        /// </summary>
        /// <remarks>
        /// The composition arc is <c>Reference</c>, not <c>Instance</c>: an
        /// instanceable prim turns its descendants into a shared prototype, which
        /// cannot carry the per-set rotation, colour and position that make the sets
        /// look like independent machines.
        /// </remarks>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async ValueTask MaterialisePlantAggregationAsync(CancellationToken cancellationToken)
        {
            if (m_powerhouseStage == null || m_twins.IsEmpty)
            {
                return;
            }
            NodeState? deviceSet = FindPredefinedNode<NodeState>(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris));
            if (deviceSet == null)
            {
                return;
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            OpenUsdRepresentationState plant = SystemContext.CreateRepresentation(
                deviceSet, m_powerhouseStage.NodeId, GeneratorsScopePrimPath, ns);

            plant.AddComponentBinding(
                SystemContext,
                ns,
                "GeneratorSets",
                new Guid("8f2a1c40-0100-4a3b-9c11-6d5e2f7a9b10"),
                OpenUsdCardinalityEnum.Many,
                OpenUsdCompositionArcEnum.Reference,
                GeneratorsScopePrimPath,
                componentRepresentation: default,
                assetReference: GeneratorAssetReference,
                dynamic: false,
                changeEventSource: default,
                componentServerUri: null,
                componentEndpointUrl: null,
                componentTypeDefinition: NodeId.Create(
                    Opc.Ua.Generators.ObjectTypes.GeneratorSetType,
                    Opc.Ua.Generators.Namespaces.Generators,
                    Server.NamespaceUris));

            // DeviceSet was registered long before this runs, so the representation
            // has to be registered on its own. Without this it exists only as a C#
            // object hanging off an already-registered parent: RegisterInDiscovery
            // below lists it, but no client can resolve or browse it, so a connector
            // never sees the aggregation and never composes any generator geometry.
            // The sets then render as live values on prims that have nothing behind
            // them - an empty powerhouse that reports 250 kW.
            SystemContext.AssignInstanceChildNodeIds(plant);
            await AddPredefinedNodeAsync(SystemContext, plant, cancellationToken)
                .ConfigureAwait(false);

            if (m_openUsdRoot?.Representations is FolderState registry)
            {
                plant.RegisterInDiscovery(registry);
            }
        }

        /// <summary>
        /// Points a twin at the simulation that drives it.
        /// </summary>
        /// <param name="set">The generator set.</param>
        /// <param name="simulation">The set's simulation.</param>
        private void AttachSimulationToTwin(GeneratorSetState set, GeneratorSimulation simulation)
        {
            if (m_twins.TryGetValue(set.NodeId, out GeneratorTwin? twin))
            {
                twin.Simulation = simulation;
                lock (m_registrationLock)
                {
                    m_liveSignals =
                    [
                        .. m_liveSignals,
                        (twin.FanAngle!, () => twin.FanAngleDegrees),
                        (twin.LoadNeedle!, () => twin.LoadNeedleDegrees),
                        (twin.TempNeedle!, () => twin.TempNeedleDegrees),
                        (twin.ExhaustCelsius!, () => twin.Simulation?.ExhaustCelsius ?? 25.0),
                        (twin.RadiatorCelsius!, () => twin.Simulation?.CoolantCelsius ?? 25.0),
                    ];
                }
            }
        }

        /// <summary>
        /// Publishes the render-only signals of every set.
        /// </summary>
        /// <remarks>
        /// Reads an immutable snapshot. A set can be added at runtime through
        /// <c>CreateGeneratorSetAsync</c>, and mutating a list while the tick
        /// enumerates it would throw on the timer thread and stop the simulation for
        /// every set.
        /// </remarks>
        private void PublishOpenUsdSignals()
        {
            DateTime now = DateTime.UtcNow;
            foreach ((BaseVariableState variable, Func<double> getter) in
                Volatile.Read(ref m_liveSignals))
            {
                variable.Value = getter();
                variable.Timestamp = now;
                variable.ClearChangeMasks(SystemContext, includeChildren: false);
            }

            foreach (GeneratorTwin twin in m_twins.Values)
            {
                GeneratorSimulation? sim = twin.Simulation;
                if (sim == null)
                {
                    continue;
                }
                UpdateBoolean(twin.ProtectionTripped, sim.ProtectionTripped, now);
                UpdateBoolean(twin.CoolantOverTemperature, sim.CoolantOverTemperature, now);
                UpdateBoolean(twin.LowOilPressure, sim.LowOilPressure, now);
                UpdateBoolean(twin.Running, sim.SpeedRpm > 100.0, now);
                UpdateSurface(twin.FuelSurface, sim.FuelLevelPercent, now);
                UpdateText(twin.OperatingStateName, sim.State.ToString(), now);
            }
        }

        /// <summary>
        /// Publishes a boolean indication.
        /// </summary>
        /// <param name="variable">The Variable to write.</param>
        /// <param name="value">The value.</param>
        /// <param name="now">Sample timestamp.</param>
        private void UpdateBoolean(BaseDataVariableState? variable, bool value, DateTime now)
        {
            if (variable == null)
            {
                return;
            }
            variable.Value = value;
            variable.Timestamp = now;
            variable.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        /// <summary>
        /// Publishes a text indication.
        /// </summary>
        /// <param name="variable">The Variable to write.</param>
        /// <param name="value">The value.</param>
        /// <param name="now">Sample timestamp.</param>
        private void UpdateText(BaseDataVariableState? variable, string value, DateTime now)
        {
            if (variable == null || Equals(variable.Value, value))
            {
                return;
            }
            variable.Value = value;
            variable.Timestamp = now;
            variable.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        /// <summary>
        /// Publishes the fuel surface position for a tank level.
        /// </summary>
        /// <param name="variable">The Variable to write.</param>
        /// <param name="levelPercent">Tank level, in percent.</param>
        /// <param name="now">Sample timestamp.</param>
        private void UpdateSurface(BaseDataVariableState? variable, double levelPercent, DateTime now)
        {
            if (variable == null)
            {
                return;
            }
            variable.Value = new ExtensionObject(FuelSurfaceAt(levelPercent));
            variable.Timestamp = now;
            variable.ClearChangeMasks(SystemContext, includeChildren: false);
        }
    }

    /// <summary>
    /// One generator set's OpenUSD twin.
    /// </summary>
    /// <param name="primPath">Prim path the set is composed at.</param>
    /// <param name="setNumber">One-based unit number.</param>
    internal sealed class GeneratorTwin(string primPath, int setNumber)
    {
        /// <summary>
        /// Degrees of needle travel across a full gauge dial.
        /// </summary>
        private const double GaugeSweepDegrees = 240.0;

        /// <summary>
        /// Gets the prim path the set is composed at.
        /// </summary>
        public string PrimPath { get; } = primPath;

        /// <summary>
        /// Gets the one-based unit number, which fixes the bay position.
        /// </summary>
        public int SetNumber { get; } = setNumber;

        /// <summary>
        /// Gets or sets the representation add-in for this set.
        /// </summary>
        public OpenUsdRepresentationState? Representation { get; set; }

        /// <summary>
        /// Gets or sets the simulation driving this set.
        /// </summary>
        public GeneratorSimulation? Simulation { get; set; }

        /// <summary>
        /// Gets or sets the bay-position signal.
        /// </summary>
        public BaseDataVariableState? BayPosition { get; set; }

        /// <summary>
        /// Gets or sets the cooling-fan angle signal.
        /// </summary>
        public BaseDataVariableState? FanAngle { get; set; }

        /// <summary>
        /// Gets or sets the load-gauge needle angle signal.
        /// </summary>
        public BaseDataVariableState? LoadNeedle { get; set; }

        /// <summary>
        /// Gets or sets the coolant-gauge needle angle signal.
        /// </summary>
        public BaseDataVariableState? TempNeedle { get; set; }

        /// <summary>
        /// Gets or sets the exhaust surface temperature signal, in degrees Celsius.
        /// </summary>
        public BaseDataVariableState? ExhaustCelsius { get; set; }

        /// <summary>
        /// Gets or sets the radiator surface temperature signal, in degrees Celsius.
        /// </summary>
        public BaseDataVariableState? RadiatorCelsius { get; set; }

        /// <summary>
        /// Gets or sets the fuel surface position signal.
        /// </summary>
        public BaseDataVariableState? FuelSurface { get; set; }

        /// <summary>
        /// Gets or sets the protection-tripped indication.
        /// </summary>
        public BaseDataVariableState? ProtectionTripped { get; set; }

        /// <summary>
        /// Gets or sets the coolant over-temperature indication.
        /// </summary>
        public BaseDataVariableState? CoolantOverTemperature { get; set; }

        /// <summary>
        /// Gets or sets the low oil pressure indication.
        /// </summary>
        public BaseDataVariableState? LowOilPressure { get; set; }

        /// <summary>
        /// Gets or sets the running indication.
        /// </summary>
        public BaseDataVariableState? Running { get; set; }

        /// <summary>
        /// Gets or sets the operating state name shown on the twin.
        /// </summary>
        public BaseDataVariableState? OperatingStateName { get; set; }

        /// <summary>
        /// Gets every signal Variable this twin owns.
        /// </summary>
        public IEnumerable<BaseDataVariableState?> Signals =>
        [
            BayPosition, FanAngle, LoadNeedle, TempNeedle, ExhaustCelsius,
            RadiatorCelsius, FuelSurface, ProtectionTripped, CoolantOverTemperature,
            LowOilPressure, Running, OperatingStateName,
        ];

        /// <summary>
        /// Gets the integrated cooling-fan angle, in degrees.
        /// </summary>
        public double FanAngleDegrees { get; private set; }

        /// <summary>
        /// Gets the load-gauge needle angle, in degrees.
        /// </summary>
        public double LoadNeedleDegrees =>
            -(GaugeSweepDegrees / 2.0) + (GaugeSweepDegrees *
                Numeric.Clamp((Simulation?.LoadFraction ?? 0.0) / 1.2, 0.0, 1.0));

        /// <summary>
        /// Gets the coolant-gauge needle angle, in degrees.
        /// </summary>
        public double TempNeedleDegrees =>
            -(GaugeSweepDegrees / 2.0) + (GaugeSweepDegrees * Numeric.Clamp(
                (Simulation?.CoolantCelsius ?? 0.0) / GeneratorDatasheet.Ranges.CoolantMaxCelsius,
                0.0,
                1.0));

        /// <summary>
        /// Degrees per second the fan is *shown* turning at rated speed.
        /// </summary>
        /// <remarks>
        /// Deliberately not the real rate. A fan on a 1500 rpm engine turns at
        /// 9000 degrees per second; sampled at the tick interval that is several
        /// revolutions per sample, so the published angle jumps by a near-arbitrary
        /// amount each time and the blades either strobe or sit still. Neither
        /// tells an operator the fan is running, which is the only thing this
        /// signal exists to say. The display rate is chosen so the per-tick step
        /// stays well under the blade pitch and the rotation reads as continuous.
        /// </remarks>
        private const double FanDisplayDegreesPerSecond = 52.0;

        /// <summary>
        /// Integrates the cooling-fan angle from engine speed.
        /// </summary>
        /// <param name="seconds">Elapsed time since the previous tick.</param>
        /// <remarks>
        /// The angle is integrated rather than sampled so the blades turn smoothly
        /// instead of jumping, and it is scaled to a rate a viewport can actually
        /// resolve - see <see cref="FanDisplayDegreesPerSecond"/>.
        /// </remarks>
        public void AdvanceFan(double seconds)
        {
            double rpm = Simulation?.SpeedRpm ?? 0.0;
            double fraction = rpm / GeneratorDatasheet.Engine.RatedSpeedRpm;
            FanAngleDegrees =
                (FanAngleDegrees + (fraction * FanDisplayDegreesPerSecond * seconds)) % 360.0;
        }
    }
}
