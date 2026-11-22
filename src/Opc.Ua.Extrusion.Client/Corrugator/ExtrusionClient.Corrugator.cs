// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Extrusion.Corrugator;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Typed entrypoint for OPC 40084-11.
    /// </summary>
    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Opens the OPC 40084-11 proxy with its real HaulOff inheritance.
        /// </summary>
        public Corrugator_InterfaceTypeClient Corrugator(NodeId node)
        {
            return new Corrugator_InterfaceTypeClient(Session, Require(node), Telemetry);
        }
    }
}
