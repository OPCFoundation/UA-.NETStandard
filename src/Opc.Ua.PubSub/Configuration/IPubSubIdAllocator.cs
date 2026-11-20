// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Allocates PubSub server-side transient identifiers.
    /// </summary>
    /// <remarks>
    /// Implements the externalized state contract required for Part 14
    /// §9.1.6 HA deployments. Implementations must allocate monotonically
    /// increasing values when shared by multiple server instances.
    /// </remarks>
    public interface IPubSubIdAllocator
    {
        /// <summary>
        /// Reserves a sequence of PubSub configuration identifiers.
        /// </summary>
        /// <param name="count">Number of identifiers to reserve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<ArrayOf<uint>> ReserveIdsAsync(
            ushort count,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Allocates a PubSubConfiguration FileType handle.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<uint> AllocateFileHandleAsync(CancellationToken cancellationToken = default);
    }
}
