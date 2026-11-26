// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;

namespace Opc.Ua.Redundancy.Client
{
    /// <summary>
    /// Options used to register a transparent redundant client session facade.
    /// </summary>
    public sealed class RedundantClientSessionOptions
    {
        /// <summary>
        /// Gets or sets the stable replica id.
        /// </summary>
        public string NodeId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the standby behavior for followers.
        /// </summary>
        public ClientStandbyMode Mode { get; set; } = ClientStandbyMode.Cold;

        /// <summary>
        /// Gets or sets the session factory used by the replica coordinator.
        /// </summary>
        public Func<CancellationToken, ValueTask<ManagedSession>>? CreateSessionAsync { get; set; }

        /// <summary>
        /// Gets or sets whether promotions should reuse the mirrored
        /// authentication token when possible.
        /// </summary>
        public bool EnableTokenReuse { get; set; } = true;

        /// <summary>
        /// Gets or sets the leader configuration callback.
        /// </summary>
        public Func<ManagedSession, bool, CancellationToken, ValueTask>? ConfigureLeaderAsync { get; set; }
    }
}
