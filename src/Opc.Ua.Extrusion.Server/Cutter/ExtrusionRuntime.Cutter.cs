// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Cutter;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-9 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-9 cutter with real mandatory cutting-product instances.
        /// </summary>
        public ValueTask<ExtrusionDevice<Cutter_InterfaceState>> CreateCutterAsync(
            ExtrusionDeviceOptions options,
            Action<Cutter_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Cutter", "Cutter",
                static (context, parent, name) => context.CreateInstanceOfCutter_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
