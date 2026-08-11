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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Pumps
{
    /// <summary>
    /// Wires the draft OPC UA — OpenUSD Bindings composition/aggregation model
    /// (spec §5.12–5.14) onto the server: the pump composed 1:1 of Impeller and
    /// Bearing component Objects (child prims), and a ProductionLine aggregating
    /// 1..n pumps (Many, instanceable) with a dynamically added/removed pump
    /// (model-change events) and a cross-server component (federation).
    /// </summary>
    public partial class PumpNodeManager
    {
        private BaseObjectState? m_productionLine;
        private FolderState? m_linePumps;
        private NodeId m_dynamicPumpNodeId;
        private const string LinePrimPath = "/Plant/Line1";

        // 1:1 (Child): create Impeller + Bearing component Objects on the pump, each
        // with its own representation, and declare One <Component> bindings.
        private void AttachPumpComponents(
            PumpState pump, OpenUsdRepresentationState pumpRep, ushort ns, string primPath)
        {
            (BaseObjectState _, OpenUsdRepresentationState impellerRep) = CreateRepresentedComponent(
                pump, "Impeller", pump.BrowseName.NamespaceIndex, primPath + "/Impeller", ns);
            (BaseObjectState _, OpenUsdRepresentationState bearingRep) = CreateRepresentedComponent(
                pump, "Bearing", pump.BrowseName.NamespaceIndex, primPath + "/Bearing", ns);

            CreateComponentBinding(pumpRep, ns, "ImpellerComponent",
                GuidFor("ImpellerComponent"),
                OpenUsdCardinalityEnum.One, OpenUsdCompositionArcEnum.Child,
                primPath + "/Impeller", componentRepresentation: impellerRep.NodeId);
            CreateComponentBinding(pumpRep, ns, "BearingComponent",
                GuidFor("BearingComponent"),
                OpenUsdCardinalityEnum.One, OpenUsdCompositionArcEnum.Child,
                primPath + "/Bearing", componentRepresentation: bearingRep.NodeId);
        }

        /// <summary>
        /// Composes one full-fidelity pump prim per configured pump.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The DeviceSet carries a plant-level representation anchored on
        /// <c>/Plant</c> with a single <c>Many</c> component binding scoped to
        /// <c>PumpType</c>. A connector resolves that binding against the
        /// DeviceSet's children and composes
        /// <c>/Plant/Pumps/&lt;BrowseName&gt;</c> for each pump from the served
        /// <c>pump.usda</c> component asset, so the rendered scene follows
        /// <c>--pumps N</c> without the stage having to author anything per pump.
        /// </para>
        /// <para>
        /// The arc is <c>Reference</c>, not <c>Instance</c>: an instanceable prim
        /// turns its descendants into a shared prototype, and a shared prototype
        /// cannot carry the per-pump impeller rotation, casing colour or gauge
        /// needles that make each machine read as its own.
        /// </para>
        /// </remarks>
        private async ValueTask MaterialisePlantAggregationAsync(
            CancellationToken cancellationToken)
        {
            if (m_plantStage == null)
            {
                return;
            }
            try
            {
                ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
                NodeState? deviceSet = PredefinedNodes.FindById(NodeId.Create(
                    Opc.Ua.Di.Objects.DeviceSet, DiNamespaceUri, Server.NamespaceUris));
                if (deviceSet == null)
                {
                    return;
                }

                OpenUsdRepresentationState plantRep = SystemContext.CreateRepresentation(
                    deviceSet, m_plantStage.NodeId, PlantPrimPath, ns);

                NodeId pumpTypeId = NodeId.Create(
                    Opc.Ua.Pumps.ObjectTypes.PumpType,
                    Opc.Ua.Pumps.Namespaces.Pumps,
                    Server.NamespaceUris);
                // Not dynamic: the configured pump set is fixed by --pumps N at
                // start-up, and the dynamic add/remove path is already demonstrated
                // by the ProductionLine. Declaring it dynamic here would also make
                // the connector's stale-prim reconciliation sweep every prim under
                // /Plant/Pumps — including the Impeller and Bearing component prims
                // this scope now contains — and deactivate them.
                CreateComponentBinding(plantRep, ns, "ConfiguredPumps",
                    new Guid("a1b2c3d4-0004-4000-8000-000000000001"),
                    OpenUsdCardinalityEnum.Many, OpenUsdCompositionArcEnum.Reference,
                    "Pumps",
                    assetReference: "@pump.usda@</Pump>",
                    componentTypeDefinition: pumpTypeId);

                SystemContext.AssignInstanceChildNodeIds(plantRep);
                await AddPredefinedNodeAsync(SystemContext, plantRep, cancellationToken)
                    .ConfigureAwait(false);

                FolderState? registry = m_openUsdRoot?.Representations;
                if (registry != null)
                {
                    registry.AddReference(ReferenceTypeIds.Organizes, false, plantRep.NodeId);
                    plantRep.AddReference(ReferenceTypeIds.Organizes, true, registry.NodeId);
                }

                m_logger.MaterialisedPlantAggregation(m_twins.Count);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to materialise the plant aggregation.");
            }
        }

        // 1..n + dynamic aggregation: a ProductionLine aggregating pumps. The
        // aggregation is an address-space demo only - it carries no OpenUSD
        // representation, so the rendered hall holds exactly the pumps the
        // connected server simulates and nothing else. A line pump is a static
        // topology entry, not a machine anyone is driving; rendering it put
        // phantom pumps in the twin that no client could account for.
        private async ValueTask MaterialiseProductionLineAsync(CancellationToken cancellationToken)
        {
            if (m_plantStage == null)
            {
                return;
            }
            try
            {
                ushort ns = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
                NodeState? deviceSet = PredefinedNodes.FindById(NodeId.Create(
                    Opc.Ua.Di.Objects.DeviceSet, DiNamespaceUri, Server.NamespaceUris));
                if (deviceSet == null)
                {
                    return;
                }

                var line = new BaseObjectState(deviceSet)
                {
                    SymbolicName = "ProductionLine",
                    BrowseName = new QualifiedName("ProductionLine", ns),
                    DisplayName = new LocalizedText("ProductionLine"),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
                };
                deviceSet.AddChild(line);
                line.NodeId = SystemContext.NodeIdFactory.New(SystemContext, line);

                var pumps = new FolderState(line)
                {
                    SymbolicName = "Pumps",
                    BrowseName = new QualifiedName("Pumps", ns),
                    DisplayName = new LocalizedText("Pumps"),
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType
                };
                line.AddChild(pumps);
                pumps.NodeId = SystemContext.NodeIdFactory.New(SystemContext, pumps);
                m_linePumps = pumps;

                // Two static aggregated entries (1..n baseline).
                CreateAggregatedPump(pumps, "P-201", ns);
                CreateAggregatedPump(pumps, "P-202", ns);

                SystemContext.AssignInstanceChildNodeIds(line);
                await AddPredefinedNodeAsync(SystemContext, line, cancellationToken).ConfigureAwait(false);

                // Dynamic composition: emit model-change events on runtime add/remove.
                ModelChangeEmissionEnabled = true;
                _ = RunDynamicCompositionAsync(ns);

                m_productionLine = line;
                m_logger.LogInformation("Materialised ProductionLine (aggregates 1..n pumps).");
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to materialise the ProductionLine.");
            }
        }

        // An aggregated line entry: a plain topology Object with no
        // representation, because it is not a machine the server simulates.
        private void CreateAggregatedPump(NodeState parent, string name, ushort ns)
        {
            var obj = new BaseObjectState(parent)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            parent.AddChild(obj);
            obj.NodeId = SystemContext.NodeIdFactory.New(SystemContext, obj);
        }

        // Dynamic demo (§5.13): repeatedly add a pump (emits a GeneralModelChange),
        // hold, then remove it (emits again), so a connector observes both the add and
        // the remove regardless of when it connects. Bounded so the server quiesces.
        private async Task RunDynamicCompositionAsync(ushort ns)
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(4000).ConfigureAwait(false);
                    m_dynamicPumpNodeId = await AddLinePumpAsync("P-203",
                        LinePrimPath + "/Pumps/P_203", ns).ConfigureAwait(false);
                    await Task.Delay(4000).ConfigureAwait(false);
                    if (!m_dynamicPumpNodeId.IsNull)
                    {
                        await DeleteNodeAsync(SystemContext, m_dynamicPumpNodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                        m_logger.RemovedLinePump(m_dynamicPumpNodeId);
                        m_dynamicPumpNodeId = NodeId.Null;
                    }
                }
                // Final add: leave P-203 in place so the composed stage renders it.
                await Task.Delay(2000).ConfigureAwait(false);
                m_dynamicPumpNodeId = await AddLinePumpAsync("P-203",
                    LinePrimPath + "/Pumps/P_203", ns).ConfigureAwait(false);
                m_logger.LogInformation("Dynamic composition: P-203 left added (final state).");
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Dynamic composition demo failed.");
            }
        }

        private async Task<NodeId> AddLinePumpAsync(string name, string primPath, ushort ns)
        {
            if (m_linePumps == null)
            {
                return NodeId.Null;
            }
            var pump = new BaseObjectState(null)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            _ = SystemContext.CreateRepresentation(
                pump, m_plantStage!.NodeId, primPath, ns);

            NodeId newId = await CreateNodeAsync(SystemContext, m_linePumps.NodeId,
                ReferenceTypeIds.Organizes, new QualifiedName(name, ns), pump, CancellationToken.None)
                .ConfigureAwait(false);
            m_logger.AddedLinePump(name, newId);
            return newId;
        }

        private (BaseObjectState, OpenUsdRepresentationState) CreateRepresentedComponent(
            NodeState parent, string name, ushort objNs, string primPath, ushort openUsdNs,
            NodeId refType = default)
        {
            var obj = new BaseObjectState(parent)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, objNs),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = refType.IsNull ? ReferenceTypeIds.HasComponent : refType,
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            parent.AddChild(obj);
            obj.NodeId = SystemContext.NodeIdFactory.New(SystemContext, obj);

            OpenUsdRepresentationState rep = SystemContext.CreateRepresentation(
                obj, m_plantStage!.NodeId, primPath, openUsdNs);
            return (obj, rep);
        }

        // Thin adapter over the reusable Opc.Ua.OpenUsd.Server authoring API: the
        // component/composition-binding logic lives in the SDK
        // (OpenUsdRepresentationAuthoring.AddComponentBinding), not in this sample.
        private void CreateComponentBinding(
            OpenUsdRepresentationState rep, ushort ns, string name, Guid bindingDefinitionId,
            OpenUsdCardinalityEnum cardinality, OpenUsdCompositionArcEnum arc, string targetPrimPath,
            NodeId componentRepresentation = default, string? assetReference = null,
            bool dynamic = false, NodeId changeEventSource = default,
            string? componentServerUri = null, string? componentEndpointUrl = null,
            NodeId componentTypeDefinition = default)
        {
            _ = rep.AddComponentBinding(
                SystemContext, ns, name, bindingDefinitionId, cardinality, arc, targetPrimPath,
                componentRepresentation, assetReference, dynamic, changeEventSource,
                componentServerUri, componentEndpointUrl, componentTypeDefinition);
        }
    }

    internal static partial class OpenUsdCompositionLog
    {
        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.OpenUsdComposition + 1,
            Level = LogLevel.Information,
            Message = "Dynamic composition: removed line pump (NodeId={NodeId}); model-change emitted.")]
        public static partial void RemovedLinePump(this ILogger logger, NodeId nodeId);

        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.OpenUsdComposition + 2,
            Level = LogLevel.Information,
            Message = "Dynamic composition: added line pump '{Name}' (NodeId={NodeId}); model-change emitted.")]
        public static partial void AddedLinePump(this ILogger logger, string name, NodeId nodeId);

        [LoggerMessage(EventId = PumpDeviceIntegrationServerEventIds.OpenUsdComposition + 3,
            Level = LogLevel.Information,
            Message = "Plant aggregation composes {PumpCount} configured pump(s) from pump.usda.")]
        public static partial void MaterialisedPlantAggregation(this ILogger logger, int pumpCount);
    }
}
