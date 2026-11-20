// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Extrusion.Extruder;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-3.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-3 extruder proxy.
        /// </summary>
        public Extruder_InterfaceTypeClient Extruder(NodeId node)
        {
            return new Extruder_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
