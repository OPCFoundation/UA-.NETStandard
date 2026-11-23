// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Stores PubSub component runtime states.
    /// </summary>
    /// <remarks>
    /// Component identifiers are deterministic address-space identifiers
    /// such as <c>pubsub:connection:Connection1</c>.
    /// </remarks>
    public interface IPubSubRuntimeStateStore
    {
        /// <summary>
        /// Reads the state for a component.
        /// </summary>
        /// <param name="componentId">Deterministic component identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PubSubState?> GetStateAsync(
            string componentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the state for a component.
        /// </summary>
        /// <param name="componentId">Deterministic component identifier.</param>
        /// <param name="state">PubSub state.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the state for a component using a lease fencing token to reject stale active
        /// writers when the caller has already resolved ownership.
        /// </summary>
        /// <param name="componentId">Deterministic component identifier.</param>
        /// <param name="state">PubSub state.</param>
        /// <param name="fencingToken">Current lease fencing token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            long fencingToken,
            CancellationToken cancellationToken = default);
    }
}
