/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Extrusion.Cutter;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-9.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-9 cutter proxy.
        /// </summary>
        public Cutter_InterfaceTypeClient Cutter(NodeId node)
        {
            return new Cutter_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
