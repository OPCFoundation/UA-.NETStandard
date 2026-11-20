/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.HaulOff;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-4 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-4 haul-off with its actual clamp and drive capabilities.
        /// </summary>
        public ValueTask<ExtrusionDevice<HaulOff_InterfaceState>> CreateHaulOffAsync(
            ExtrusionDeviceOptions options,
            Action<HaulOff_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "HaulOff", "Haul-off",
                static (context, parent, name) => context.CreateInstanceOfHaulOff_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
