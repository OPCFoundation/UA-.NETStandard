// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Extrusion.MeltPump;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-5.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-5 melt-pump proxy.
        /// </summary>
        public MeltPump_InterfaceTypeClient MeltPump(NodeId node)
        {
            return new MeltPump_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
