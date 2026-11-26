// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS
{
    /// <summary>
    /// Selects native nested declarations through their generated instance factories rather than cloning type nodes.
    /// </summary>
    public static class GmsEquipmentNodeExtensions
    {
        /// <summary>
        /// Selects the exact Double Qualified instance without materializing unspecified optional life thresholds.
        /// The containing runtime must supply its remaining-time sample and IsCountingUp=false observation.
        /// </summary>
        public static SensorState AddQualified(this SensorState sensor, ISystemContext context)
        {
            if (sensor == null)
            {
                throw new ArgumentNullException(nameof(sensor));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            BaseObjectState parent = sensor.ToolLife ?? throw new ServiceResultException(
                StatusCodes.BadTypeDefinitionInvalid, "A native sensor must contain ToolLife.");
            var name = new QualifiedName(BrowseNames.Qualified,
                (ushort)context.NamespaceUris.GetIndex(GmsModel.NamespaceUri));
            BaseInstanceState? existing = parent.FindChild(context, name);
            if (existing != null)
            {
                if (existing is not ToolLifeState<double>)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "Qualified must retain its generated Double ToolLife declaration.");
                }
                return sensor;
            }
            ToolLifeState<double> life = context.CreateSensorType_ToolLife_Qualified(parent, forInstance: true);
            parent.AddChild(life);
            NodeId previous = context.AssignInstanceNodeId(life);
            context.AssignInstanceChildNodeIds(life, previous);
            return sensor;
        }
    }
}
