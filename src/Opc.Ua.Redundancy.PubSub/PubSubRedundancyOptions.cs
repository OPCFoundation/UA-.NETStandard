/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Election mechanism used to elect the active instance of a redundant PubSub set.
    /// </summary>
    public enum PubSubRedundancyElection
    {
        /// <summary>
        /// Whole-instance active/standby driven by a shared
        /// <see cref="Ua.Redundancy.ILeaderElection"/> (Raft or Kubernetes lease).
        /// </summary>
        LeaderElection,

        /// <summary>
        /// Per-component leases held in a shared-store
        /// <see cref="IPubSubLeaseStore"/> (compare-and-swap with fencing tokens).
        /// </summary>
        LeaseStore
    }

    /// <summary>
    /// Options for wiring distributed PubSub high-availability (OPC UA Part 14 §9.1.6).
    /// </summary>
    public sealed class PubSubRedundancyOptions
    {
        /// <summary>
        /// Gets or sets the redundancy behaviour a standby instance keeps warm.
        /// </summary>
        /// <remarks>
        /// <see cref="PubSubRedundancyMode.Cold"/> keeps only activation/election shared and
        /// recreates process-local runtime state after promotion. <see cref="PubSubRedundancyMode.Warm"/>
        /// additionally shares runtime state so the standby stays configured/paused. Only
        /// <see cref="PubSubRedundancyMode.Hot"/> also shares writer checkpoints for
        /// SequenceNumber continuity.
        /// </remarks>
        public PubSubRedundancyMode Mode { get; set; } = PubSubRedundancyMode.Warm;

        /// <summary>
        /// Gets or sets the election mechanism used to pick the active instance.
        /// </summary>
        public PubSubRedundancyElection Election { get; set; } = PubSubRedundancyElection.LeaderElection;

        /// <summary>
        /// Gets or sets the stable identity of this instance used for lease ownership.
        /// </summary>
        public string OwnerId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the lease time-to-live used by the lease-store election.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(15);
    }
}
