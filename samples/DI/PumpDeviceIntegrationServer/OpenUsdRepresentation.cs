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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;
using Opc.Ua.Pumps;

namespace Pumps
{
    /// <summary>
    /// Wires the draft OPC UA — OpenUSD Bindings companion model onto the Pumps
    /// server: the well-known OpenUSD facility (stage + representation registries),
    /// a PlantStage descriptor, and an OpenUsdRepresentation AddIn on Pump #1 with
    /// three read-only live bindings (Part 2, UaToUsdTelemetry).
    /// </summary>
    public partial class PumpNodeManager
    {
        private OpenUsdRootState? m_openUsdRoot;
        private OpenUsdStageState? m_plantStage;

        /// <summary>
        /// Everything the OpenUSD twin needs to render one simulated pump: the
        /// prim the pump composes into, the hand-built signal Variables that
        /// feed its bindings, and the simulation those signals read. One
        /// instance exists per configured pump, so a server started with
        /// <c>--pumps N</c> drives N independently animated machines.
        /// </summary>
        private sealed class PumpTwin
        {
            public PumpTwin(string primPath, int pumpNumber)
            {
                PrimPath = primPath;
                PumpNumber = pumpNumber;
            }

            /// <summary>
            /// Absolute prim path of this pump on the plant stage.
            /// </summary>
            public string PrimPath { get; }

            /// <summary>
            /// One-based number of the simulated unit.
            /// </summary>
            public int PumpNumber { get; }

            public BaseDataVariableState? AlarmActive { get; set; }

            public BaseDataVariableState? ShaftAngle { get; set; }

            public BaseDataVariableState? SpeedSetpoint { get; set; }

            public BaseDataVariableState? BayPosition { get; set; }

            /// <summary>
            /// Position of the liquid surface in the suction vessel, published as a
            /// structured coordinate so the Translation profile can drive it.
            /// </summary>
            public BaseDataVariableState? FluidSurface { get; set; }

            public OpenUsdRepresentationState? Representation { get; set; }

            /// <summary>
            /// The pump this twin renders, needed to resolve nodes the fluent pass
            /// creates after the pump has been materialised.
            /// </summary>
            public PumpState? Pump { get; set; }

            /// <summary>
            /// The simulation this twin renders. Assigned by
            /// <c>RegisterPumpSimulation</c>, which runs after the pump has been
            /// materialised, so it stays null until the fluent pass completes.
            /// </summary>
            public PumpSimulationState? Simulation { get; set; }

            /// <summary>
            /// Shaft angular position in degrees, or zero before the simulation
            /// is wired.
            /// </summary>
            public double ShaftAngleDegrees => Simulation?.ShaftAngleDegrees ?? 0.0;

            /// <summary>
            /// The supervision alarm state the status-light binding follows.
            /// </summary>
            public bool AlarmActiveState => Simulation?.AlarmActive ?? false;
        }

        // Written while the address space is built and while a pump is added at
        // runtime; read by the 250 ms simulation tick.
        private readonly ConcurrentDictionary<NodeId, PumpTwin> m_twins = new();

        /// <summary>
        /// Rate at which the server samples the hand-built OpenUSD signals, in
        /// milliseconds. Matches the simulation tick so every change is observed.
        /// </summary>
        private const double SignalSamplingIntervalMilliseconds = 250;

        /// <summary>
        /// Registers the Variables that are hand-built onto the pump rather than
        /// materialised by the generated factory. A generated instance state only
        /// reports its declared members, so these children are never picked up by
        /// the walk that <c>AddPredefinedNodeAsync(pump)</c> performs: they browse
        /// and read correctly but are never sampled, which leaves any binding that
        /// uses them frozen at its start-up value.
        /// </summary>
        /// <param name="pump">The pump whose signals to register.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>
        /// Registers one pump, not every pump materialised so far. This runs once
        /// per pump as it is created, and the twin is already in the dictionary by
        /// then, so walking the whole dictionary would re-register every earlier
        /// pump and add its shaft angle to the publish set again - leaving the
        /// first pump published N times per tick for N pumps.
        /// </remarks>
        private async ValueTask RegisterOpenUsdSignalsAsync(
            PumpState pump, CancellationToken cancellationToken)
        {
            if (!m_twins.TryGetValue(pump.NodeId, out PumpTwin? twin))
            {
                return;
            }

            foreach (BaseDataVariableState? signal in new[]
            {
                twin.AlarmActive, twin.ShaftAngle, twin.SpeedSetpoint,
                twin.BayPosition, twin.FluidSurface
            })
            {
                if (signal != null)
                {
                    await AddPredefinedNodeAsync(SystemContext, signal, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // Push the shaft angle from the same loop that drives the generated
            // measurement Variables, so it uses the one path proven to raise
            // data-change notifications. The closure captures this pump's twin,
            // so every pump reports its own angle.
            if (twin.ShaftAngle != null)
            {
                TrackSignal(twin.ShaftAngle, () => twin.ShaftAngleDegrees);
            }
        }

        private const string PlantRootLayerIdentifier = "asset-repo/Plant.usd";

        /// <summary>
        /// Prim scope the configured pumps are composed into. Each pump owns
        /// <c>&lt;PumpsScopePrimPath&gt;/&lt;BrowseName&gt;</c>, which the plant
        /// aggregation composes from the served <c>pump.usda</c> component asset.
        /// </summary>
        private const string PumpsScopePrimPath = "/Plant/Pumps";

        /// <summary>
        /// Plant prim the DeviceSet-level representation anchors on.
        /// </summary>
        private const string PlantPrimPath = "/Plant";

        /// <summary>
        /// Spacing between two pump bays along the plant Y axis, in metres. Wide
        /// enough to clear the 1.80 m baseplate with an access aisle.
        /// </summary>
        private const double PumpBaySpacingMetres = 2.4;

        /// <summary>
        /// Absolute prim path of the supplied pump.
        /// </summary>
        private static string PumpPrimPathFor(PumpState pump)
        {
            return PumpsScopePrimPath + "/" + (pump.BrowseName.Name ?? "Pump");
        }

        private async ValueTask MaterialiseOpenUsdFacilityAsync(
            CancellationToken cancellationToken)
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

                m_plantStage = SystemContext.CreateInstanceOfOpenUsdStageType(
                    stages, new QualifiedName("PlantStage", ns));
                stages.AddChild(m_plantStage);
                m_plantStage.CreateOrReplaceRootLayerIdentifier(SystemContext, null!)
                    .Value = PlantRootLayerIdentifier;

                // §5.2 Twin-BOM content integrity: the digest is computed over the
                // *resolved root-layer content*, never over the identifier string, so a
                // connector can detect tampering of the bytes it actually composes.
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
                m_plantStage.CreateOrReplaceRootLayerDigest(
                    SystemContext,
                    SystemContext.CreateOpenUsdStageType_RootLayerDigest(m_plantStage, forInstance: true))
                    .Value = (ByteString)digest;
                m_plantStage.CreateOrReplaceRootLayerDigestAlgorithm(
                    SystemContext,
                    SystemContext.CreateOpenUsdStageType_RootLayerDigestAlgorithm(m_plantStage, forInstance: true))
                    .Value = OpenUsdDigestAlgorithmEnum.Sha256;

                // Per the companion spec §4.2 the OpenUSD facility SHALL be a
                // component of the Server Object (i=2253) so that any conformant
                // connector can Browse Server -> OpenUSD -> Representations. Record
                // the inverse reference here; the matching forward reference from
                // the Server Object (owned by the core node manager) is added via
                // the externalReferences dictionary in LinkOpenUsdRootToServer.
                root.AddReference(ReferenceTypeIds.HasComponent, true, Opc.Ua.ObjectIds.Server);

                // §5.15 asset content delivery (OU-AssetDelivery): serve this stage's
                // artist-authored USD layer closure so a connector can render the twin
                // with no external asset resolver.
                UsdAssetDelivery.AttachStageAssets(SystemContext, m_plantStage, ns, servedAssets);

                SystemContext.AssignInstanceChildNodeIds(root);
                await AddPredefinedNodeAsync(SystemContext, root, cancellationToken)
                    .ConfigureAwait(false);

                m_openUsdRoot = root;
                m_logger.MaterialisedOpenUsdFacility(root.NodeId, m_plantStage.NodeId);
            }
            catch (Exception ex)
            {
                m_plantStage = null;
                m_openUsdRoot = null;
                m_logger.LogError(ex, "Failed to materialise the OpenUSD facility.");
            }
        }

        // Loads the embedded artist-authored USD layers this server serves (spec §5.15).
        private static List<ServedAsset> LoadServedAssets()
        {
            return new List<ServedAsset>
            {
                new ServedAsset("Plant.usda", OpenUsdAssetKindEnum.RootLayer, ReadEmbeddedAsset("Plant.usda")),
                new ServedAsset("pump.usda", OpenUsdAssetKindEnum.Reference, ReadEmbeddedAsset("pump.usda")),
            };
        }

        private static byte[] ReadEmbeddedAsset(string resourceName)
        {
            using Stream? s = typeof(PumpNodeManager).Assembly.GetManifestResourceStream(resourceName);
            if (s == null)
            {
                return Array.Empty<byte>();
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
            // base.CreateAddressSpaceAsync loads predefined nodes and runs
            // OnAddressSpaceReadyAsync, which materialises the OpenUSD facility.
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            // Now that the root exists, add the forward HasComponent reference from
            // the Server Object (i=2253, owned by the core node manager) to it.
            LinkOpenUsdRootToServer(externalReferences);
        }

        // Adds the Server -> OpenUSD forward reference into the externalReferences
        // dictionary the master node manager applies across managers, so the
        // well-known facility is browsable from the Server Object (spec §4.2).
        private void LinkOpenUsdRootToServer(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (m_openUsdRoot == null)
            {
                return;
            }
            if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.Server, out IList<IReference>? references)
                || references == null)
            {
                externalReferences[Opc.Ua.ObjectIds.Server] = references = new List<IReference>();
            }
            references.Add(new NodeStateReference(
                ReferenceTypeIds.HasComponent, false, m_openUsdRoot.NodeId));
            m_logger.LogInformation(
                "Linked OpenUSD facility under the Server Object (i=2253).");
        }

        // Call before AddPredefinedNodeAsync(pump), so the binding source
        // NodeIds captured here are the per-instance ones the generated
        // CreateOrReplace/AddXxx helpers assigned.
        private void AttachOpenUsdRepresentation(PumpState pump, int pumpNumber)
        {
            if (m_plantStage == null)
            {
                return;
            }
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            string primPath = PumpPrimPathFor(pump);
            var twin = new PumpTwin(primPath, pumpNumber);

            OpenUsdRepresentationState rep = SystemContext.CreateRepresentation(
                pump, m_plantStage.NodeId, primPath, ns);
            twin.Representation = rep;
            twin.Pump = pump;

            MeasurementsState? m = pump.Operational?.Measurements;
            NodeId bearingTemp = m?.BearingTemperature?.NodeId ?? NodeId.Null;
            NodeId diffPressure = m?.DifferentialPressure?.NodeId ?? NodeId.Null;
            NodeId fluidTemp = m?.FluidTemperature?.NodeId ?? NodeId.Null;
            NodeId massFlow = m?.MassFlow?.NodeId ?? NodeId.Null;
            NodeId efficiency = m?.PumpEfficiency?.NodeId ?? NodeId.Null;
            NodeId level = m?.Level?.NodeId ?? NodeId.Null;
            NodeId numberOfStarts = m?.NumberOfStarts?.NodeId ?? NodeId.Null;

            // Layout: each pump publishes the bay it stands in, bound as a
            // translation on its own prim. That keeps the shared component asset
            // position-free, so any number of pumps lays out without editing it.
            twin.BayPosition = CreatePumpVariable(
                pump,
                "BayPosition",
                Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                Variant.From(new ExtensionObject(new ThreeDCartesianCoordinates
                {
                    X = 0.0,
                    Y = (pumpNumber - 1) * PumpBaySpacingMetres,
                    Z = 0.0
                })),
                writable: false);
            CreateBinding(rep, ns, "BayLayout",
                GuidFor("BayLayout"),
                twin.BayPosition.NodeId, primPath, "xformOp:translate", "double3",
                OpenUsdRenderTargetKindEnum.Translation, 1.0);

            // 0.2 UaAlarmToUsd: a supervision alarm active-state drives the status
            // light visibility. A dedicated Boolean variable exposes the alarm
            // aspect the simulation toggles (see AdvanceSimulation).
            twin.AlarmActive = CreatePumpVariable(
                pump, "AlarmActive", Opc.Ua.DataTypeIds.Boolean, new Variant(false), writable: false);
            // Serve the live flag from the simulation. The Variable is registered
            // in its own right by RegisterOpenUsdSignalsAsync, which is what lets
            // a monitored item sample it.
            twin.AlarmActive.MinimumSamplingInterval = SignalSamplingIntervalMilliseconds;
            PumpTwin alarmTwin = twin;
            twin.AlarmActive.OnSimpleReadValue =
                (ISystemContext context, NodeState node, ref Variant value) =>
                {
                    value = new Variant(alarmTwin.AlarmActiveState);
                    return ServiceResult.Good;
                };

            // Shaft angular position. This is what makes the twin *look* like it
            // is running: MassFlow is a rate, so binding it straight to a rotation
            // op only ever produces a fixed fraction of a degree. Integrating it
            // into an angle gives the impeller and coupling a continuous spin.
            twin.ShaftAngle = CreatePumpVariable(
                pump, "ShaftAngle", Opc.Ua.DataTypeIds.Double, new Variant(0.0), writable: false);
            twin.ShaftAngle.MinimumSamplingInterval = SignalSamplingIntervalMilliseconds;
            PumpTwin shaftTwin = twin;
            twin.ShaftAngle.OnSimpleReadValue =
                (ISystemContext context, NodeState node, ref Variant value) =>
                {
                    value = new Variant(shaftTwin.ShaftAngleDegrees);
                    return ServiceResult.Good;
                };

            // Shaft position -> impeller rotation. MassFlow is a *rate*, so binding
            // it straight to a rotation op pins the shaft at a fraction of a degree;
            // the simulation integrates it into an angle instead. The scale slows
            // the render to a legible ~45 degrees per second - a real 2900 rpm shaft
            // would alias into a stroboscopic blur at any practical sampling rate.
            CreateBinding(rep, ns, "ShaftSpin",
                GuidFor("ShaftSpin"),
                twin.ShaftAngle.NodeId, primPath + "/Impeller", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, ShaftRenderScale,
                sourceSemanticId: MassFlowSemanticId);

            // The motor cooling fan turns with the duty point, so a pump that is
            // barely moving fluid is visibly loafing.
            CreateBinding(rep, ns, "MotorFanSpin",
                GuidFor("MotorFanSpin"),
                twin.ShaftAngle.NodeId, primPath + "/Motor/FanBlades", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, ShaftRenderScale * FanToShaftRatio);

            // BearingTemperature is published in Kelvin (OPC 40223), but the
            // DisplayColor render target ramps blue -> red over the datasheet
            // bearing-temperature range, so the binding declares the Kelvin shift.
            CreateBinding(rep, ns, "BearingTempColor",
                GuidFor("BearingTempColor"),
                bearingTemp, primPath + "/Body", "primvars:displayColor", "color3f[]",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0,
                offset: -KelvinOffset);

            // Bearing-temperature gauge: 0 degrees at the bottom of the datasheet
            // range, sweeping SweepDegrees over the whole range.
            CreateBinding(rep, ns, "BearingTempNeedle",
                GuidFor("BearingTempNeedle"),
                bearingTemp, primPath + "/PowerEnd/TempGauge/Needle", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation,
                GaugeSweepDegrees / (PumpDatasheet.Ranges.BearingTemperatureMax -
                    PumpDatasheet.Ranges.BearingTemperatureMin),
                offset: -GaugeSweepDegrees * PumpDatasheet.Ranges.BearingTemperatureMin /
                    (PumpDatasheet.Ranges.BearingTemperatureMax -
                        PumpDatasheet.Ranges.BearingTemperatureMin));

            // Discharge pressure gauge needle over the datasheet pressure range.
            CreateBinding(rep, ns, "DischargePressureNeedle",
                GuidFor("DischargePressureNeedle"),
                diffPressure, primPath + "/Discharge/Gauge/Needle", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation,
                GaugeSweepDegrees / PumpDatasheet.Ranges.DifferentialPressureMax);

            // Suction vessel: the connector converts a scalar level into a scalar,
            // and xformOp:translate needs a double3, so the simulation publishes the
            // surface position as a structured coordinate the Translation profile
            // accepts. The surface disc rides on top of the liquid.
            twin.FluidSurface = CreatePumpVariable(
                pump,
                "FluidSurfacePosition",
                Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                Variant.From(new ExtensionObject(FluidSurfaceAt(
                    PumpDatasheet.Simulation.LevelNominal))),
                writable: false);
            twin.FluidSurface.MinimumSamplingInterval = SignalSamplingIntervalMilliseconds;
            CreateBinding(rep, ns, "SuctionLevelRise",
                GuidFor("SuctionLevelRise"),
                twin.FluidSurface.NodeId, primPath + "/SuctionVessel/Surface",
                "xformOp:translate", "double3",
                OpenUsdRenderTargetKindEnum.Translation, 1.0);

            // Efficiency and mass flow carry no render semantics of their own -- the
            // DisplayColor ramp models a temperature, so colouring efficiency with it
            // would read as a lie. They are surfaced as attributes a viewer shows on
            // selection, which is what makes the twin inspectable.
            CreateBinding(rep, ns, "EfficiencyReadout",
                GuidFor("EfficiencyReadout"),
                efficiency, primPath + "/Motor/Nameplate", "inputs:pumpEfficiency", "double",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);

            // Suction line tint follows the pumped fluid temperature. The
            // DisplayColor ramp runs blue -> red over 20..100 degrees Celsius, so
            // the binding declares the Kelvin shift and nothing else.
            CreateBinding(rep, ns, "FluidTempColor",
                GuidFor("FluidTempColor"),
                fluidTemp, primPath + "/Suction/Neck/Mat/Surface", "inputs:diffuseColor", "color3f",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0,
                offset: -KelvinOffset);

            CreateBinding(rep, ns, "MassFlowReadout",
                GuidFor("MassFlowReadout"),
                massFlow, primPath + "/Motor/Nameplate", "inputs:massFlow", "double",
                OpenUsdRenderTargetKindEnum.Custom, 1.0,
                sourceSemanticId: MassFlowSemanticId);
            CreateBinding(rep, ns, "NumberOfStartsReadout",
                GuidFor("NumberOfStartsReadout"),
                numberOfStarts, primPath + "/Motor/Nameplate", "inputs:numberOfStarts", "double",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);

            AttachAlarmBindings(pump, rep, ns, primPath, twin);

            // 0.2 UsdToUaCommand (opt-in): a writable speed setpoint Variable is the
            // command target. The binding is Controllable and present, but a
            // connector only issues the write when explicitly enabled AND authorized
            // (single-writer, fail-closed). Enabled=true means "declared", NOT
            // "auto-actuated" — the opt-in lives on the connector, not on Enabled.
            // §5.10/§9 authorization posture: the command target is *capable* of being
            // written (AccessLevel), but the write right is withheld by default and
            // granted only to an authenticated (non-anonymous) session — a Server
            // "withholds by default" the RolePermissions a connector must hold before
            // issuing any command.
            twin.SpeedSetpoint = CreatePumpVariable(
                pump, "SpeedSetpoint", Opc.Ua.DataTypeIds.Double, new Variant(0.0), writable: true);
            twin.SpeedSetpoint.OnReadUserAccessLevel = OnReadCommandTargetUserAccessLevel;
            CreateBinding(rep, ns, "SpeedSetpointCommand",
                GuidFor("SpeedSetpointCommand"),
                NodeId.Null, primPath + "/Impeller", "inputs:speedSetpoint", "double",
                kind: null, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdCommandBindingType,
                signalRole: OpenUsdSignalRoleEnum.Controllable,
                commandTargetNodeId: twin.SpeedSetpoint.NodeId,
                commandTriggerPropertyName: "inputs:speedSetpoint");

            // Composition (§5.12): the pump is composed of an Impeller and a Bearing,
            // each a component Object with its own representation, mapped 1:1 to a child
            // prim (arc=Child). This adds <Component> bindings on the pump representation.
            AttachPumpComponents(pump, rep, ns, primPath);

            SystemContext.AssignInstanceChildNodeIds(rep);
            m_twins[pump.NodeId] = twin;
        }

        /// <summary>
        /// Binds the per-pump supervision states, so the twin distinguishes
        /// <i>which</i> fault a pump has - and which pump has it - rather than
        /// showing one undifferentiated halo.
        /// </summary>
        private void AttachAlarmBindings(
            PumpState pump,
            OpenUsdRepresentationState rep,
            ushort ns,
            string primPath,
            PumpTwin twin)
        {
            SupervisionState? events = pump.Events;
            NodeId cavitation = events?.SupervisionProcessFluid?.Cavitation?.NodeId ?? NodeId.Null;
            NodeId motorOverheat = events?.SupervisionPumpOperation?.MotorOverheat?.NodeId ?? NodeId.Null;

            // Any active supervision state draws a red circle on the floor around
            // the machine. A ring reads from anywhere in the hall and from any
            // camera angle; a lamp on a mast only reads when you happen to be
            // looking straight at it.
            CreateBinding(rep, ns, "AlarmRingVisibility",
                GuidFor("AlarmRingVisibility"),
                twin.AlarmActive!.NodeId, primPath + "/AlarmRing", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

            // Which fault, at the place on the machine where it actually is: the
            // ring says a pump is in alarm, these say why.
            CreateBinding(rep, ns, "CavitationHalo",
                GuidFor("CavitationHalo"),
                cavitation, primPath + "/Suction/CavitationHalo", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);
            CreateBinding(rep, ns, "OverheatHalo",
                GuidFor("OverheatHalo"),
                motorOverheat, primPath + "/OverheatHalo", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

            // The OverTempAlarm condition itself is deliberately not bound. The
            // fluent alarm builder leaves the condition's state children on their
            // standard namespace-0 declaration NodeIds - which
            // PumpInstanceNodeIdRegressionTests pins - so every pump's alarm shares
            // one ActiveState, Severity and AckedState node. Binding those would
            // ring every pump in the hall at once. The per-pump supervision states
            // above are the alarm indication instead: they are genuinely per
            // instance, and they are what drives the condition through
            // ActivatesAlarm in the first place.
        }

        /// <summary>
        /// Position of the suction-vessel liquid surface for a published level.
        /// </summary>
        private static ThreeDCartesianCoordinates FluidSurfaceAt(double levelMetres)
        {
            return new ThreeDCartesianCoordinates
            {
                X = 0.0,
                Y = 0.0,
                Z = levelMetres * FluidSurfaceScale
            };
        }

        /// <summary>
        /// Stable declaration identifiers for the pump bindings. The binding model
        /// defines the effective runtime identity as
        /// (represented object, BindingDefinitionId) and the id itself as a
        /// declaration identifier, "NOT a runtime instance key" — so every pump
        /// declares the same ids and its own representation disambiguates them.
        /// </summary>
        private static readonly Dictionary<string, Guid> s_bindingDefinitionIds =
            new(StringComparer.Ordinal)
            {
                ["ShaftSpin"] = new Guid("6e63cf2c-f2de-4f78-a8f8-f0ccdbb7647a"),
                ["BearingTempColor"] = new Guid("b1a1f6f0-5c2b-5a1e-9f3a-2b7c4d8e0011"),
                ["AlarmRingVisibility"] = new Guid("d3c3b8f2-7e4d-5c30-b15c-4d9e6a0b2233"),
                ["SpeedSetpointCommand"] = new Guid("e4d4c9a3-8f5e-5d41-c26d-5e0f7b1c3344"),
                ["ImpellerComponent"] = new Guid("a1b2c3d4-0001-4000-8000-000000000001"),
                ["BearingComponent"] = new Guid("a1b2c3d4-0001-4000-8000-000000000002"),
                ["BayLayout"] = new Guid("a1b2c3d4-0005-4000-8000-000000000001"),
                ["MotorFanSpin"] = new Guid("a1b2c3d4-0005-4000-8000-000000000002"),
                ["BearingTempNeedle"] = new Guid("a1b2c3d4-0005-4000-8000-000000000003"),
                ["DischargePressureNeedle"] = new Guid("a1b2c3d4-0005-4000-8000-000000000004"),
                ["SuctionLevelRise"] = new Guid("a1b2c3d4-0005-4000-8000-000000000005"),
                ["EfficiencyReadout"] = new Guid("a1b2c3d4-0005-4000-8000-000000000006"),
                ["FluidTempColor"] = new Guid("a1b2c3d4-0005-4000-8000-000000000007"),
                ["MassFlowReadout"] = new Guid("a1b2c3d4-0005-4000-8000-000000000008"),
                ["NumberOfStartsReadout"] = new Guid("a1b2c3d4-0005-4000-8000-000000000009"),
                ["CavitationHalo"] = new Guid("a1b2c3d4-0005-4000-8000-00000000000a"),
                ["OverheatHalo"] = new Guid("a1b2c3d4-0005-4000-8000-00000000000b")
            };

        /// <summary>
        /// The declaration identifier of the named binding.
        /// </summary>
        private static Guid GuidFor(string binding)
        {
            return s_bindingDefinitionIds[binding];
        }

        /// <summary>
        /// Renders the integrated shaft angle at a legible speed: a real 2900 rpm
        /// shaft aliases into a stroboscopic blur at any practical sampling rate.
        /// </summary>
        private const double ShaftRenderScale = 0.0025;

        /// <summary>
        /// The cooling fan sits on the same shaft, so it turns at the same speed;
        /// rendering it slightly faster keeps the two visually distinguishable.
        /// </summary>
        private const double FanToShaftRatio = 1.6;

        /// <summary>
        /// Angular sweep of a gauge needle across its full scale, in degrees.
        /// </summary>
        private const double GaugeSweepDegrees = 270.0;

        /// <summary>
        /// Kelvin-to-Celsius shift the colour render targets expect.
        /// </summary>
        private const double KelvinOffset = 273.15;

        /// <summary>
        /// Metres of modelled vessel height per metre of published level. The
        /// suction vessel is drawn at a fifth of its real height so it does not
        /// tower over the machine it feeds.
        /// </summary>
        private const double FluidSurfaceScale = 0.2;

        // ECLASS-style IRDI for "volume flow rate" — a portable semantic id a
        // connector can use to resolve the source across vendors (0.2 SemanticSource).
        private const string MassFlowSemanticId = "0173-1#02-AAO677#002";

        /// <summary>
        /// §5.10/§9: the write right a command target requires is <b>withheld by
        /// default</b> — an anonymous session sees a read-only UserAccessLevel and its
        /// write is rejected. Only a session that authenticated (and therefore holds a
        /// Role beyond <c>Anonymous</c>) sees CurrentWrite.
        /// </summary>
        private static ServiceResult OnReadCommandTargetUserAccessLevel(
            ISystemContext context, NodeState node, ref byte value)
        {
            value = IsAuthenticatedSession(context)
                ? AccessLevels.CurrentReadOrWrite
                : AccessLevels.CurrentRead;
            return ServiceResult.Good;
        }

        private static bool IsAuthenticatedSession(ISystemContext context)
        {
            IUserIdentity? identity = (context as ISessionSystemContext)?.UserIdentity;
            if (identity == null || identity.TokenType == UserTokenType.Anonymous)
            {
                return false;
            }
            ArrayOf<NodeId> roles = identity.GrantedRoleIds;
            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i] != Opc.Ua.ObjectIds.WellKnownRole_Anonymous)
                {
                    return true;
                }
            }
            return false;
        }

        // Creates a simple Variable child on the pump (used for the 0.2 command
        // setpoint and alarm-active demo signals), assigning a per-instance NodeId
        // immediately: the node is hand-built, so no generated helper assigns one.
        private BaseDataVariableState CreatePumpVariable(
            PumpState pump, string name, NodeId dataType, Variant initialValue, bool writable)
        {
            byte access = writable
                ? AccessLevels.CurrentReadOrWrite
                : AccessLevels.CurrentRead;
            var v = new BaseDataVariableState(pump)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, pump.BrowseName.NamespaceIndex),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = access,
                UserAccessLevel = access,
                Value = initialValue,
            };
            pump.AddChild(v);
            v.NodeId = SystemContext.NodeIdFactory.New(SystemContext, v);
            return v;
        }

        /// <summary>
        /// Registers every pump's representation in the well-known
        /// <c>Server/OpenUSD/Representations</c> registry. A connector discovers
        /// twins through that registry alone, so a representation that is not
        /// organised there is invisible no matter how completely it is authored.
        /// </summary>
        private void OrganiseRepresentations()
        {
            FolderState? registry = m_openUsdRoot?.Representations;
            if (registry == null)
            {
                return;
            }
            foreach (PumpTwin twin in m_twins.Values)
            {
                OpenUsdRepresentationState? rep = twin.Representation;
                if (rep == null)
                {
                    continue;
                }
                registry.AddReference(ReferenceTypeIds.Organizes, false, rep.NodeId);
                rep.AddReference(ReferenceTypeIds.Organizes, true, registry.NodeId);
            }
        }

        // Thin adapter over the reusable Opc.Ua.OpenUsd.Server authoring API: binds the
        // plant stage and forwards. The binding-authoring logic lives in the SDK
        // (OpenUsdRepresentationAuthoring.AddLiveBinding), not in this sample.
        private void CreateBinding(
            OpenUsdRepresentationState rep, ushort ns, string name,
            Guid bindingDefinitionId, NodeId sourceNodeId, string targetPrimPath,
            string targetPropertyName, string targetUsdTypeName,
            OpenUsdRenderTargetKindEnum? kind, double scale,
            uint bindingTypeId = Opc.Ua.OpenUsd.ObjectTypes.OpenUsdValueChangeBindingType,
            OpenUsdSignalRoleEnum signalRole = OpenUsdSignalRoleEnum.Observable,
            string? sourceSemanticId = null,
            OpenUsdAlarmAspectEnum? alarmAspect = null,
            NodeId commandTargetNodeId = default,
            string? commandTriggerPropertyName = null,
            double offset = 0.0)
        {
            OpenUsdLiveBindingState binding = rep.AddLiveBinding(
                SystemContext, ns, m_plantStage!.NodeId, name, bindingDefinitionId, sourceNodeId,
                targetPrimPath, targetPropertyName, targetUsdTypeName, kind, scale,
                bindingTypeId, signalRole, sourceSemanticId, alarmAspect,
                commandTargetNodeId, commandTriggerPropertyName);

            // 5.8 applies Scale then Offset. AddLiveBinding only sets Scale, so an
            // additive term (such as the Kelvin -> Celsius shift the DisplayColor
            // render target expects) has to be authored here.
            if (offset != 0.0)
            {
                binding.CreateOrReplaceOffset(
                    SystemContext,
                    SystemContext.CreateOpenUsdLiveBindingType_Offset(binding, forInstance: true))
                    .Value = offset;
            }
        }
    }

    internal static partial class OpenUsdRepresentationLog
    {
        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.OpenUsdRepresentation + 1,
            Level = LogLevel.Information,
            Message = "Materialised OpenUSD facility (root {RootId}, PlantStage {StageId}).")]
        public static partial void MaterialisedOpenUsdFacility(this ILogger logger, NodeId rootId, NodeId stageId);
    }
}
