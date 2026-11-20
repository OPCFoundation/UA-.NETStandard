// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Extrusion.Calibrator;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-10.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the exact OPC 40084-10 calibrator proxy.
        /// </summary>
        public Calibrator_InterfaceTypeClient Calibrator(NodeId node)
        {
            return new Calibrator_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
