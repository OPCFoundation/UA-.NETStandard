// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Raft.Transport;

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
        /// Required for network Raft. If using per-pod DNS, publish not-ready addresses so peers can elect a
        /// leader before the server becomes ready.
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
        /// Gets or sets the local Raft listener IP address, optionally with a port. A <c>tcp://</c> prefix is
        /// accepted. Defaults to the <c>POD_IP</c> environment variable and <see cref="RaftPort"/>.
        /// </summary>
        public string? BindAddress { get; set; }

        /// <summary>
        /// Gets or sets the required network certificate resolver. It receives the service provider, Raft member id
        /// (StatefulSet ordinal plus one), whether that member is local, and the startup cancellation token.
        /// Return the local leaf with its private key or the expected public leaf of a remote member. Each call
        /// transfers ownership of its certificate to the backend. Resolve certificates from trusted configuration
        /// or a certificate provider, never from a network peer during the handshake. Observe the cancellation
        /// token so startup and shutdown do not wait indefinitely.
        /// </summary>
        public Func<IServiceProvider, ulong, bool, CancellationToken, ValueTask<X509Certificate2>>?
            ResolveMemberCertificateAsync { get; set; }

        /// <summary>
        /// Gets or sets an optional peer endpoint resolver. It receives the service provider, remote member id and
        /// default per-pod DNS endpoint. Resolved endpoints are fixed until restart. Use a stable per-member
        /// Service address when pod IPs can change; never use a Service that can route to different Raft members.
        /// </summary>
        public Func<IServiceProvider, ulong, DnsEndPoint, CancellationToken, ValueTask<IPEndPoint>>?
            ResolvePeerEndPointAsync { get; set; }

        /// <summary>
        /// Gets or sets the framing, admission, and additional certificate policy for the mutual TLS transport.
        /// </summary>
        public TlsRaftTransportOptions TransportOptions { get; set; } = new()
        {
            MaxFrameLength = kMinimumTransportFrameLength
        };

        /// <summary>
        /// Gets or sets whether to use a volatile in-process backend instead of network Raft. This is only
        /// supported for explicit single-replica tests outside Kubernetes.
        /// </summary>
        public bool UseInProcessTestTransport { get; set; }

        /// <summary>
        /// Gets or sets whether to use a crash-safe file WAL. Required and enabled by default for network Raft.
        /// <see cref="UseInProcessTestTransport"/> always uses in-memory storage.
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
        /// Gets or sets the startup deadline for resolving certificates and peer addresses, and separately
        /// how long a started replica waits for an initial leader before proceeding.
        /// </summary>
        public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Allows the bounded UA Raft snapshot and its protocol envelope to fit in one transport frame.
        /// </summary>
        internal const int kMinimumTransportFrameLength = 16 * 1024 * 1024;
    }
}
