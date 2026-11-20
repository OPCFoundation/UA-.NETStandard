/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Extrusion.Filter;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-6.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the actual OPC 40084-6 filter proxy.
        /// </summary>
        public Filter_InterfaceTypeClient Filter(NodeId node)
        {
            return new Filter_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
