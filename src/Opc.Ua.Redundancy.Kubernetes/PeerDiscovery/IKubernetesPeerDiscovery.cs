/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: discovers OPC UA peer ServerUris from Kubernetes EndpointSlices.
    /// </summary>
    public interface IKubernetesPeerDiscovery
    {
        /// <summary>
        /// Raised when the discovered peer ServerUri set changes.
        /// </summary>
        event Action<ArrayOf<string>>? PeerServerUrisChanged;

        /// <summary>
        /// Gets the last discovered peer ServerUris.
        /// </summary>
        ArrayOf<string> PeerServerUris { get; }

        /// <summary>
        /// Refreshes peer discovery once.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The discovered peer ServerUris.</returns>
        ValueTask<ArrayOf<string>> RefreshAsync(CancellationToken ct = default);
    }
}
