// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using Opc.Ua.Di;
using Opc.Ua.Extrusion.Calender;
using Opc.Ua.Extrusion.Calibrator;
using Opc.Ua.Extrusion.Corrugator;
using Opc.Ua.Extrusion.Cutter;
using Opc.Ua.Extrusion.Die;
using Opc.Ua.Extrusion.Extruder;
using Opc.Ua.Extrusion.Filter;
using Opc.Ua.Extrusion.HaulOff;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.MeltPump;
using Opc.Ua.Extrusion.Pelletizer;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Loads all published OPC 40084 v2 models into the existing Machinery owner.
    /// </summary>
    public sealed class ExtrusionModelProvider : IMachineryModelProvider
    {
        /// <inheritdoc/>
        public int Order => 220;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
        [
            PlasticsRubber.Namespaces.PlasticsRubber,
            Extrusion.Namespaces.Extrusion,
            Line.Namespaces.ExtrusionLine,
            Extruder.Namespaces.ExtrusionExtruder,
            HaulOff.Namespaces.ExtrusionHaulOff,
            MeltPump.Namespaces.ExtrusionMeltPump,
            Filter.Namespaces.ExtrusionFilter,
            Die.Namespaces.ExtrusionDie,
            Pelletizer.Namespaces.ExtrusionPelletizer,
            Cutter.Namespaces.ExtrusionCutter,
            Calibrator.Namespaces.ExtrusionCalibrator,
            Corrugator.Namespaces.ExtrusionCorrugator,
            Calender.Namespaces.ExtrusionCalender
        ];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!Contains(Machinery.ObjectIds.Machines))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Extrusion requires MachineryParts.BuildingBlocks.");
            }
            if (!Contains(PlasticsRubber.ObjectTypeIds.StartDeviceType))
            {
                nodes.AddOpcUaPlasticsRubber(context);
            }
            if (!Contains(Extrusion.ObjectTypeIds.ExtrusionDeviceType))
            {
                nodes.AddOpcUaExtrusion(context);
            }
            if (!Contains(Line.ObjectTypeIds.ExtrusionLine_InterfaceType))
            {
                nodes.AddOpcUaExtrusionLine(context);
            }
            if (!Contains(Extruder.ObjectTypeIds.Extruder_InterfaceType))
            {
                nodes.AddOpcUaExtrusionExtruder(context);
            }
            if (!Contains(HaulOff.ObjectTypeIds.HaulOff_InterfaceType))
            {
                nodes.AddOpcUaExtrusionHaulOff(context);
            }
            if (!Contains(MeltPump.ObjectTypeIds.MeltPump_InterfaceType))
            {
                nodes.AddOpcUaExtrusionMeltPump(context);
            }
            if (!Contains(Filter.ObjectTypeIds.Filter_InterfaceType))
            {
                nodes.AddOpcUaExtrusionFilter(context);
            }
            if (!Contains(Die.ObjectTypeIds.Die_InterfaceType))
            {
                nodes.AddOpcUaExtrusionDie(context);
            }
            if (!Contains(Pelletizer.ObjectTypeIds.Pelletizer_InterfaceType))
            {
                nodes.AddOpcUaExtrusionPelletizer(context);
            }
            if (!Contains(Cutter.ObjectTypeIds.Cutter_InterfaceType))
            {
                nodes.AddOpcUaExtrusionCutter(context);
            }
            if (!Contains(Calibrator.ObjectTypeIds.Calibrator_InterfaceType))
            {
                nodes.AddOpcUaExtrusionCalibrator(context);
            }
            if (!Contains(Corrugator.ObjectTypeIds.Corrugator_InterfaceType))
            {
                nodes.AddOpcUaExtrusionCorrugator(context);
            }
            if (!Contains(Calender.ObjectTypeIds.Calender_InterfaceType))
            {
                nodes.AddOpcUaExtrusionCalender(context);
            }

            context.NodeStateFactory
                .AddOpcUaDi()
                .AddOpcUaIA()
                .AddOpcUaMachinery()
                .AddOpcUaPlasticsRubber()
                .AddOpcUaExtrusion()
                .AddOpcUaExtrusionLine()
                .AddOpcUaExtrusionExtruder()
                .AddOpcUaExtrusionHaulOff()
                .AddOpcUaExtrusionMeltPump()
                .AddOpcUaExtrusionFilter()
                .AddOpcUaExtrusionDie()
                .AddOpcUaExtrusionPelletizer()
                .AddOpcUaExtrusionCutter()
                .AddOpcUaExtrusionCalibrator()
                .AddOpcUaExtrusionCorrugator()
                .AddOpcUaExtrusionCalender();
            context.EncodeableFactory.Builder
                .AddOpcUaPlasticsRubber()
                .AddOpcUaExtrusion()
                .AddOpcUaExtrusionLine()
                .AddOpcUaExtrusionExtruder()
                .AddOpcUaExtrusionFilter()
                .AddOpcUaExtrusionCutter()
                .Commit();

            bool Contains(ExpandedNodeId id)
            {
                NodeId local = ExpandedNodeId.ToNodeId(id, context.NamespaceUris);
                return nodes.Any(node => node.NodeId == local);
            }
        }
    }
}
