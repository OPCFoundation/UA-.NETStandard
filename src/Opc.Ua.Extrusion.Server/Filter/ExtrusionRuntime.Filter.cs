// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Filter;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// OPC 40084-6 device construction on the shared runtime.
    /// </summary>
    public sealed partial class ExtrusionRuntime
    {
        /// <summary>
        /// Creates a genuine OPC 40084-6 filter; no disputed profile or fictional melt pump is advertised.
        /// </summary>
        public ValueTask<ExtrusionDevice<Filter_InterfaceState>> CreateFilterAsync(
            ExtrusionDeviceOptions options,
            Action<Filter_InterfaceState>? configure = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(options, "Filter", "Filter",
                static (context, parent, name) => context.CreateInstanceOfFilter_InterfaceType(parent, name),
                configure, cancellationToken);
        }
    }
}
