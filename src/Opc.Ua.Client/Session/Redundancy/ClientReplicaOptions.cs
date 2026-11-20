/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Options for a client replica that participates in a replica set.
    /// </summary>
    public sealed record class ClientReplicaOptions
    {
        /// <summary>
        /// Stable identity of this replica; used as the lease holder id.
        /// </summary>
        public string NodeId { get; init; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Standby behavior for followers.
        /// </summary>
        public ClientStandbyMode Mode { get; init; } = ClientStandbyMode.Cold;

        /// <summary>
        /// Shared-store key under which the leader publishes its protected session
        /// secrets so a promoted follower can reuse the AuthenticationToken.
        /// </summary>
        public string SessionRecordKey { get; init; } = "client-replica/session";

        /// <summary>
        /// Reuse the leader's mirrored AuthenticationToken on promotion against a
        /// HotAndMirrored server instead of recreating the session. The standby
        /// still performs the full ActivateSession signature validation.
        /// </summary>
        public bool EnableTokenReuse { get; init; } = true;

        /// <summary>
        /// Validates that a connected standby endpoint is an authorized mirrored member for
        /// the endpoint saved in the leader handoff. Without a validator, token reuse requires
        /// an exact endpoint URL match.
        /// </summary>
        public Func<EndpointDescription, EndpointDescription, CancellationToken, ValueTask<bool>>?
            ValidateMirroredEndpointAsync { get; init; }

        /// <summary>
        /// Creates a connected managed session for this replica. The coordinator
        /// connects per <see cref="Mode"/> (immediately for Warm/Hot, on promotion
        /// for Cold). Subscriptions are applied by <see cref="ConfigureLeaderAsync"/>.
        /// </summary>
        public Func<CancellationToken, ValueTask<ManagedSession>>? CreateSessionAsync { get; init; }

        /// <summary>
        /// Applies the leader's subscriptions / publishing to the active session.
        /// Invoked once a replica becomes leader; receives whether the session was
        /// fast-activated by token reuse (subscriptions are mirrored and may only
        /// need publishing enabled) or freshly created.
        /// </summary>
        public Func<ManagedSession, bool, CancellationToken, ValueTask>? ConfigureLeaderAsync { get; init; }
    }
}
