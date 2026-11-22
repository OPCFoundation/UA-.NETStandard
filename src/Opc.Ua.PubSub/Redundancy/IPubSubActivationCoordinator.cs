// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Coordinates which PubSub components are active on this instance in a
    /// redundant (high-availability) deployment, per OPC UA Part 14 §9.1.6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A coordinator answers, per component, whether this instance should be
    /// <see cref="PubSubComponentRole.Active"/> (drive the transport) or
    /// <see cref="PubSubComponentRole.Standby"/> (wait to take over), and
    /// raises <see cref="RoleChanged"/> when that decision changes so the
    /// runtime can pause or resume the component.
    /// </para>
    /// <para>
    /// The default registration is <see cref="AlwaysActiveCoordinator"/>,
    /// which reports every component active so non-redundant deployments are
    /// unaffected. Redundant deployments register a shared-store-backed
    /// coordinator (for example a lease-based one) via dependency injection.
    /// Implementations are a provider extension point and must be injectable.
    /// </para>
    /// </remarks>
    public interface IPubSubActivationCoordinator
    {
        /// <summary>
        /// Starts coordination (for example begins acquiring and renewing
        /// leadership leases). Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops coordination and relinquishes any acquired leadership.
        /// Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the current role of the identified component on this
        /// instance.
        /// </summary>
        /// <param name="componentId">
        /// Deterministic component identifier (for example
        /// <c>pubsub:writergroup:WriterGroup1</c>).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The component's current role.</returns>
        ValueTask<PubSubComponentRole> GetRoleAsync(
            string componentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Raised whenever the role of a component changes on this instance.
        /// </summary>
        event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged;
    }
}
