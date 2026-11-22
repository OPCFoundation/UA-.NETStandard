// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Extrusion.Die;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-7.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-7 die proxy.
        /// </summary>
        public Die_InterfaceTypeClient Die(NodeId node)
        {
            return new Die_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
