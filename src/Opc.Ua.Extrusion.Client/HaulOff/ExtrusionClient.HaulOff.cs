// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Extrusion.HaulOff;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-4.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-4 haul-off proxy.
        /// </summary>
        public HaulOff_InterfaceTypeClient HaulOff(NodeId node)
        {
            return new HaulOff_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
