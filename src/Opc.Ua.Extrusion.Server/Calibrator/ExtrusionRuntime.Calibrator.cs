// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Calibrator;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-10 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-10 calibrator with selectable positioning, water and vacuum capabilities.
        /// </summary>
        public ValueTask<ExtrusionDevice<Calibrator_InterfaceState>> CreateCalibratorAsync(
            ExtrusionDeviceOptions options,
            Action<Calibrator_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Calibrator", "Calibrator",
                static (context, parent, name) => context.CreateInstanceOfCalibrator_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
