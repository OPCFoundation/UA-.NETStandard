// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Configures a Machinery address space after the predefined models are
    /// available.
    /// </summary>
    public interface IMachineryConfigurator
    {
        /// <summary>
        /// Configures the active Machinery build context.
        /// </summary>
        ValueTask ConfigureAsync(
            IMachineryBuildContext context,
            CancellationToken cancellationToken);
    }
}
