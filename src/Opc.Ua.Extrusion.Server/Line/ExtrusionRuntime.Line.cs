/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Line;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-2 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-2 line and its selected production/job interfaces.
        /// </summary>
        public ValueTask<ExtrusionDevice<ExtrusionLine_InterfaceState>> CreateLineAsync(
            ExtrusionDeviceOptions options,
            Action<ExtrusionLine_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "ExtrusionLine", "Extrusion line",
                static (context, parent, name) => context.CreateInstanceOfExtrusionLine_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
