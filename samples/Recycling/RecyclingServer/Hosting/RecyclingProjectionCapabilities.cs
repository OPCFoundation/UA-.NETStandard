// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Withholds structural companion claims from read-only process projections.
    /// Runtime feature presence alone cannot prove the unavailable production-control contract.
    /// The independent AMB sidecar keeps its actual asset-discovery claims.
    /// </summary>
    internal sealed class RecyclingProjectionCapabilities(IRecyclingProcessViewAdapter adapter) : IServerStartupTask
    {
        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server, CancellationToken cancellationToken = default)
        {
            if (adapter is not CompanionProcessViewAdapter)
            {
                return;
            }
            if (server is not ServerInternalData data)
            {
                throw new InvalidOperationException(
                    "The read-only sample requires the stock server's conformance publication manager.");
            }
            foreach (MachineryNodeManager owner in server.FindNodeManagers<MachineryNodeManager>())
            {
                data.ConformanceUnitsManager.Unregister(owner);
            }
            await data.ConformanceUnitsManager.PublishAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
