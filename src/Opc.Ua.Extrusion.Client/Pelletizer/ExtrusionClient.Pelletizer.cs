/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Extrusion.Pelletizer;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-8.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-8 pelletizer proxy.
        /// </summary>
        public Pelletizer_InterfaceTypeClient Pelletizer(NodeId node)
        {
            return new Pelletizer_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
