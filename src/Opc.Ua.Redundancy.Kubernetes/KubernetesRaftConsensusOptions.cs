// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for a Kubernetes-hosted multi-node Raft consensus replica
    /// (<see cref="KubernetesRaftBuilderExtensions.UseKubernetesRaftConsensus"/>). Membership is static: a
    /// <c>StatefulSet</c> of <see cref="ReplicaCount"/> pods with stable ordinals, each mapping to a Raft node id.
    /// </summary>
    public sealed class KubernetesRaftConsensusOptions
    {
        /// <summary>
        /// Gets or sets this pod's name (used to derive the StatefulSet ordinal → Raft node id). Defaults to
        /// <see cref="Environment.MachineName"/>, which is the pod name in a Kubernetes StatefulSet.
        /// </summary>
        public string? PodName { get; set; }

        /// <summary>
        /// Gets or sets the StatefulSet name used to build peer DNS names. Defaults to <see cref="PodName"/> with the
        /// trailing <c>-{ordinal}</c> removed.
        /// </summary>
        public string? StatefulSetName { get; set; }

        /// <summary>
        /// Gets or sets the headless Service name that resolves per-pod DNS (<c>{statefulset}-{i}.{headless}</c>).
        /// Required.
        /// </summary>
        public string HeadlessServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Kubernetes namespace used for fully-qualified peer DNS
        /// (<c>{statefulset}-{i}.{headless}.{namespace}.svc</c>). When <c>null</c>, the short in-namespace form is
        /// used.
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Gets or sets the static cluster size (number of StatefulSet replicas). Required; use an odd count (3 or 5)
        /// for a fault-tolerant quorum.
        /// </summary>
        public int ReplicaCount { get; set; }

        /// <summary>
        /// Gets or sets the TCP port the Raft transport binds and dials.
        /// </summary>
        public int RaftPort { get; set; } = 5560;

        /// <summary>
        /// Gets or sets the local Raft transport bind address. Defaults to <c>tcp://0.0.0.0:{RaftPort}</c>.
        /// </summary>
        public string? BindAddress { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a crash-safe file (WAL) store is used on a PersistentVolume so a
        /// restarted pod rejoins from its log. When <c>false</c>, a volatile in-memory store is used (a restarted pod
        /// re-syncs the full log from the leader). Defaults to <c>true</c>.
        /// </summary>
        public bool UseDurableStorage { get; set; } = true;

        /// <summary>
        /// Gets or sets the directory (PersistentVolume path) for the Raft WAL when
        /// <see cref="UseDurableStorage"/> is <c>true</c>.
        /// </summary>
        public string StoragePath { get; set; } = "/var/lib/opcua/raft";

        /// <summary>
        /// Gets or sets a value indicating whether WAL flushes force data through the OS cache (durability vs.
        /// throughput).
        /// </summary>
        public bool Fsync { get; set; } = true;

        /// <summary>
        /// Gets or sets the wall-clock interval between Raft ticks.
        /// </summary>
        public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Gets or sets the number of ticks before an election timeout (must exceed <see cref="HeartbeatTick"/>).
        /// </summary>
        public int ElectionTick { get; set; } = 10;

        /// <summary>
        /// Gets or sets the number of ticks between leader heartbeats.
        /// </summary>
        public int HeartbeatTick { get; set; } = 1;

        /// <summary>
        /// Gets or sets a value indicating whether the disruption-free pre-vote protocol is enabled (recommended).
        /// </summary>
        public bool PreVote { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether a leader steps down without quorum contact (recommended).
        /// </summary>
        public bool CheckQuorum { get; set; } = true;

        /// <summary>
        /// Gets or sets how long startup waits for an initial leader before proceeding.
        /// </summary>
        public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
