// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Creates typed per-peripheral workflows over this facade's existing session and independent WMTP selection.
        /// </summary>
        public WmtpPeripheralClient GetWirelessPeripheral(NodeId peripheralId)
        {
            return new WmtpPeripheralClient(this, GetPeripheralProxy(peripheralId));
        }

        /// <summary>
        /// Reuses the facade's error-preserving optional-child resolver.
        /// </summary>
        internal ValueTask<ArrayOf<NodeId>> ResolveWmtpChildrenAsync(
            NodeId parent, ArrayOf<QualifiedName> names, CancellationToken cancellationToken)
        {
            EnsureActive();
            return ResolveChildNodesAsync(parent, names, cancellationToken);
        }

        /// <summary>
        /// Reuses the facade's continuation-point-safe browse workflow.
        /// </summary>
        internal IAsyncEnumerable<ReferenceDescription> BrowseWmtpChildrenAsync(
            NodeId parent, CancellationToken cancellationToken)
        {
            EnsureActive();
            return BrowseChildrenAsync(parent, cancellationToken);
        }

        /// <summary>
        /// Keeps child workflows within the parent facade's lifetime.
        /// </summary>
        internal void EnsureWmtpActive()
        {
            EnsureActive();
        }
    }
}
