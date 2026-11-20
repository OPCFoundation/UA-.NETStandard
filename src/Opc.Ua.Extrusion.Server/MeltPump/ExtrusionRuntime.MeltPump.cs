/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.MeltPump;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-5 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-5 melt pump.
        /// </summary>
        public ValueTask<ExtrusionDevice<MeltPump_InterfaceState>> CreateMeltPumpAsync(
            ExtrusionDeviceOptions options,
            Action<MeltPump_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "MeltPump", "Melt pump",
                static (context, parent, name) => context.CreateInstanceOfMeltPump_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
