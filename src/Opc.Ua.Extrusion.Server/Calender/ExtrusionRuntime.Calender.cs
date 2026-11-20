// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Calender;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-12 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates an OPC 40084-12 calender with selected rolls, gaps and product coupling groups.
        /// </summary>
        public ValueTask<ExtrusionDevice<Calender_InterfaceState>> CreateCalenderAsync(
            ExtrusionDeviceOptions options,
            Action<Calender_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Calender", "Calender",
                static (context, parent, name) => context.CreateInstanceOfCalender_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
