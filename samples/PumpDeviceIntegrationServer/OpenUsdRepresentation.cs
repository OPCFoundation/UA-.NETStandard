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
        private BaseDataVariableState? m_alarmActiveVar;
        private BaseDataVariableState? m_speedSetpointVar;

        private const string PlantRootLayerIdentifier = "asset-repo/Plant.usd";
        private const string PumpPrimPath = "/Plant/Pumps/P101";

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

                // 0.2 Twin-BOM content integrity: publish a deterministic digest of
                // the resolved root layer identity so a connector can verify the
                // stage before composing it. A production server digests the actual
                // resolved bytes; here we digest the identifier as a testable stand-in.
                byte[] digest;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    digest = sha.ComputeHash(
                        System.Text.Encoding.UTF8.GetBytes(PlantRootLayerIdentifier));
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
                UsdAssetDelivery.AttachStageAssets(SystemContext, m_plantStage, ns, LoadServedAssets());

                AssignChildNodeIds(root);
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
                new ServedAsset("remote-pump.usda", OpenUsdAssetKindEnum.Reference, ReadEmbeddedAsset("remote-pump.usda")),
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

        // Call between AssignChildNodeIds(pump) and AddPredefinedNodeAsync(pump).
        private void AttachOpenUsdRepresentation(PumpState pump)
        {
            if (m_plantStage == null)
            {
                return;
            }
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);

            OpenUsdRepresentationState rep = SystemContext
                .CreateInstanceOfOpenUsdRepresentationType(
                    pump, new QualifiedName("OpenUsdRepresentation", ns));
            // The instance factory leaves ReferenceTypeId = Null; set HasComponent
            // so the AddIn is browsable from the represented object.
            rep.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            pump.AddChild(rep);
            rep.NodeId = SystemContext.NodeIdFactory.New(SystemContext, rep);

            rep.CreateOrReplaceStage(SystemContext, null!).Value = m_plantStage.NodeId;
            rep.CreateOrReplacePrimPath(SystemContext, null!).Value = PumpPrimPath;

            MeasurementsState? m = pump.Operational?.Measurements;
            NodeId? massFlow = m?.MassFlow?.NodeId;
            NodeId? bearingTemp = m?.BearingTemperature?.NodeId;
            NodeId? diffPressure = m?.DifferentialPressure?.NodeId;

            CreateBinding(rep, ns, "MassFlowSpin",
                new Guid("6e63cf2c-f2de-4f78-a8f8-f0ccdbb7647a"),
                massFlow, "/Plant/Pumps/P101/Impeller", "xformOp:rotateZ", "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0,
                sourceSemanticId: MassFlowSemanticId);
            CreateBinding(rep, ns, "BearingTempColor",
                new Guid("b1a1f6f0-5c2b-5a1e-9f3a-2b7c4d8e0011"),
                bearingTemp, "/Plant/Pumps/P101/Body", "primvars:displayColor", "color3f",
                OpenUsdRenderTargetKindEnum.DisplayColor, 1.0);
            CreateBinding(rep, ns, "DiffPressureEmissive",
                new Guid("c2b2a7e1-6d3c-5b2f-a04b-3c8d5e9f1122"),
                diffPressure, "/Plant/Pumps/P101/StatusLight/Mat/Surface", "inputs:emissiveColor", "color3f",
                OpenUsdRenderTargetKindEnum.EmissiveColor, 1.0);

            // 0.2 UaAlarmToUsd: a supervision alarm active-state drives the status
            // light visibility. A dedicated Boolean variable exposes the alarm
            // aspect the simulation toggles (see AdvanceSimulation).
            m_alarmActiveVar = CreatePumpVariable(
                pump, "AlarmActive", Opc.Ua.DataTypeIds.Boolean, new Variant(false), writable: false);
            CreateBinding(rep, ns, "AlarmActiveVisibility",
                new Guid("d3c3b8f2-7e4d-5c30-b15c-4d9e6a0b2233"),
                m_alarmActiveVar.NodeId, "/Plant/Pumps/P101/StatusLight", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

            // 0.2 UsdToUaCommand (opt-in): a writable speed setpoint Variable is the
            // command target. The binding is Controllable and present, but a
            // connector only issues the write when explicitly enabled AND authorized
            // (single-writer, fail-closed). Enabled=true means "declared", NOT
            // "auto-actuated" — the opt-in lives on the connector, not on Enabled.
            m_speedSetpointVar = CreatePumpVariable(
                pump, "SpeedSetpoint", Opc.Ua.DataTypeIds.Double, new Variant(0.0), writable: true);
            CreateBinding(rep, ns, "SpeedSetpointCommand",
                new Guid("e4d4c9a3-8f5e-5d41-c26d-5e0f7b1c3344"),
                null, "/Plant/Pumps/P101/Impeller", "inputs:speedSetpoint", "double",
                kind: null, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdCommandBindingType,
                signalRole: OpenUsdSignalRoleEnum.Controllable,
                commandTargetNodeId: m_speedSetpointVar.NodeId,
                commandTriggerPropertyName: "inputs:speedSetpoint");

            // Composition (§5.12): the pump is composed of an Impeller and a Bearing,
            // each a component Object with its own representation, mapped 1:1 to a child
            // prim (arc=Child). This adds <Component> bindings on the pump representation.
            AttachPumpComponents(pump, rep, ns);

            AssignChildNodeIds(rep);
        }

        // ECLASS-style IRDI for "volume flow rate" — a portable semantic id a
        // connector can use to resolve the source across vendors (0.2 SemanticSource).
        private const string MassFlowSemanticId = "0173-1#02-AAO677#002";

        // Creates a simple Variable child on the pump (used for the 0.2 command
        // setpoint and alarm-active demo signals), assigning a per-instance NodeId
        // immediately because AssignChildNodeIds(pump) already ran.
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

        private void OrganiseRepresentation(PumpState pump)
        {
            FolderState? registry = m_openUsdRoot?.Representations;
            if (registry == null)
            {
                return;
            }
            foreach (BaseInstanceState child in EnumerateChildren(pump))
            {
                if (child is OpenUsdRepresentationState rep)
                {
                    registry.AddReference(ReferenceTypeIds.Organizes, false, rep.NodeId);
                    rep.AddReference(ReferenceTypeIds.Organizes, true, registry.NodeId);
                }
            }
        }

        private System.Collections.Generic.List<BaseInstanceState> EnumerateChildren(NodeState parent)
        {
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            return children;
        }

        // Thin adapter over the reusable Opc.Ua.OpenUsd.Server authoring API: binds the
        // plant stage and forwards. The binding-authoring logic lives in the SDK
        // (OpenUsdRepresentationAuthoring.AddLiveBinding), not in this sample.
        private void CreateBinding(
            OpenUsdRepresentationState rep, ushort ns, string name,
            Guid bindingDefinitionId, NodeId? sourceNodeId, string targetPrimPath,
            string targetPropertyName, string targetUsdTypeName,
            OpenUsdRenderTargetKindEnum? kind, double scale,
            uint bindingTypeId = Opc.Ua.OpenUsd.ObjectTypes.OpenUsdValueChangeBindingType,
            OpenUsdSignalRoleEnum signalRole = OpenUsdSignalRoleEnum.Observable,
            string? sourceSemanticId = null,
            OpenUsdAlarmAspectEnum? alarmAspect = null,
            NodeId? commandTargetNodeId = null,
            string? commandTriggerPropertyName = null)
        {
            _ = rep.AddLiveBinding(
                SystemContext, ns, m_plantStage!.NodeId, name, bindingDefinitionId, sourceNodeId,
                targetPrimPath, targetPropertyName, targetUsdTypeName, kind, scale,
                bindingTypeId, signalRole, sourceSemanticId, alarmAspect,
                commandTargetNodeId, commandTriggerPropertyName);
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
