// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Die;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-7 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-7 extrusion die.
        /// </summary>
        public ValueTask<ExtrusionDevice<Die_InterfaceState>> CreateDieAsync(
            ExtrusionDeviceOptions options,
            Action<Die_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Die", "Extruder die",
                static (context, parent, name) => context.CreateInstanceOfDie_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
