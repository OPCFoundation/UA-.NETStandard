/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Di;
using Opc.Ua.IA;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Reusable server-side helper for the OPC 40010 Robotics companion model: loads the
    /// Robotics type system into a node manager. Instantiate Robotics-typed objects with
    /// the generated <c>ISystemContext.CreateInstanceOf&lt;Type&gt;</c> factories (e.g.
    /// <c>CreateInstanceOfMotionDeviceSystemType</c>) so instances carry the full
    /// companion-type structure rather than only a type-definition reference.
    /// </summary>
    public static class RoboticsServer
    {
        /// <summary>
        /// Loads the full Robotics type system into <paramref name="nodes"/>: the
        /// OPC UA DI base model, then the IA and Robotics companion models (all
        /// source-generated), in dependency order. Call this while building the
        /// predefined-node collection of a node manager. Returns the number of
        /// nodes added by the IA + Robotics models.
        /// </summary>
        public static int AddRoboticsTypeSystem(
            this NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            nodes.AddOpcUaDi(context);
            int before = nodes.Count;
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaRobotics(context);
            return nodes.Count - before;
        }
    }
}
