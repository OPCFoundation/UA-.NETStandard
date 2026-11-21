// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Options for lease-based PubSub activation wired by
    /// <c>IPubSubBuilder.WithLeaseActivation(...)</c>. Unset values fall back
    /// to the <see cref="LeaseActivationCoordinator"/> defaults.
    /// </summary>
    public sealed class LeaseActivationOptions
    {
        /// <summary>
        /// Stable owner identity for this instance used when competing for
        /// leases. A generated identifier is used when not set.
        /// </summary>
        public string? OwnerId { get; set; }

        /// <summary>
        /// Duration a held lease remains valid before it must be renewed.
        /// </summary>
        public TimeSpan? LeaseDuration { get; set; }

        /// <summary>
        /// Interval at which the active instance renews its held leases.
        /// </summary>
        public TimeSpan? RenewInterval { get; set; }

        /// <summary>
        /// Interval at which standby instances retry acquiring a lease.
        /// </summary>
        public TimeSpan? RetryInterval { get; set; }
    }
}
