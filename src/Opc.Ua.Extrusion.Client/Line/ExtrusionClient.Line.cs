// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Extrusion.Line;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-2.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-2 line proxy.
        /// </summary>
        public ExtrusionLine_InterfaceTypeClient Line(NodeId node)
        {
            return new ExtrusionLine_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
