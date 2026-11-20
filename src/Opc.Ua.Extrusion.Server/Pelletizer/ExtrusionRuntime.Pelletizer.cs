// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Pelletizer;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-8 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-8 pelletizer and selected knife packages and die plates.
        /// </summary>
        public ValueTask<ExtrusionDevice<Pelletizer_InterfaceState>> CreatePelletizerAsync(
            ExtrusionDeviceOptions options,
            Action<Pelletizer_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Pelletizer", "Pelletizer",
                static (context, parent, name) => context.CreateInstanceOfPelletizer_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
