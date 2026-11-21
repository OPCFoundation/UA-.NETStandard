// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Default <see cref="IPubSubActivationCoordinator"/> that reports every
    /// component <see cref="PubSubComponentRole.Active"/>.
    /// </summary>
    /// <remarks>
    /// Registered by default so non-redundant deployments behave exactly as
    /// before: no component is ever placed on standby and
    /// <see cref="RoleChanged"/> never fires. Redundant deployments replace
    /// this with a shared-store-backed coordinator.
    /// </remarks>
    public sealed class AlwaysActiveCoordinator : IPubSubActivationCoordinator
    {
        /// <summary>
        /// A shared stateless instance.
        /// </summary>
        public static AlwaysActiveCoordinator Instance { get; } = new();

        /// <inheritdoc/>
        public event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubComponentRole> GetRoleAsync(
            string componentId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<PubSubComponentRole>(PubSubComponentRole.Active);
        }
    }
}
