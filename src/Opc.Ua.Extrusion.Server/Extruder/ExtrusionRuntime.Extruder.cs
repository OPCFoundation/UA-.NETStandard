// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Extruder;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-3 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-3 extruder, including configured zones, drives, feeders and hoppers.
        /// </summary>
        public ValueTask<ExtrusionDevice<Extruder_InterfaceState>> CreateExtruderAsync(
            ExtrusionDeviceOptions options,
            Action<Extruder_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Extruder", "Extruder",
                static (context, parent, name) => context.CreateInstanceOfExtruder_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
