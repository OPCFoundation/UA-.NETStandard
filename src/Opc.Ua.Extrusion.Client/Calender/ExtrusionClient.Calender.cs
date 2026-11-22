// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Extrusion.Calender;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-12.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-12 calender proxy.
        /// </summary>
        public Calender_InterfaceTypeClient Calender(NodeId node)
        {
            return new Calender_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
