// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Corrugator;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-11 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates the OPC 40084-11 corrugator, preserving its actual HaulOff inheritance.
        /// </summary>
        public ValueTask<ExtrusionDevice<Corrugator_InterfaceState>> CreateCorrugatorAsync(
            ExtrusionDeviceOptions options,
            Action<Corrugator_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Corrugator", "Corrugator",
                static (context, parent, name) => context.CreateInstanceOfCorrugator_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
