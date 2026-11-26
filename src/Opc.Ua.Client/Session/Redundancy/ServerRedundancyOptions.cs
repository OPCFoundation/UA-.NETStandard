// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Bounds the best-effort server-redundancy refresh so an unavailable
    /// server cannot delay connect, reconnect, or failover.
    /// </summary>
    /// <remarks>
    /// Both bounds trade responsiveness against tolerance of a slow server.
    /// Raise them for a high-latency link, where the defaults can expire
    /// before a healthy server answers; lower them when a connect must not
    /// stall. All operation timeouts must be positive and finite.
    /// Managed server redundancy requires a signed OPC UA SecureChannel that
    /// authenticates the expected server application URI. Transparent sets use
    /// their shared application URI. Discovery and reverse connection metadata
    /// identify where to connect, not who authenticated the channel.
    /// </remarks>
    public sealed record ServerRedundancyOptions
    {
        /// <summary>
        /// Default bound applied to each redundancy operation.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// The default interval for checking the active member's service level.
        /// </summary>
        public static readonly TimeSpan DefaultMonitoringInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Bounds reading the active server's redundancy metadata at connect,
        /// reconnect, and failover. A refresh that exceeds it retains the
        /// previous snapshot rather than discarding it.
        /// </summary>
        public TimeSpan RefreshTimeout { get; init; } = DefaultTimeout;

        /// <summary>
        /// Bounds resolving each peer's endpoint. Peers resolve in parallel, so
        /// one unreachable backup does not consume another's budget, and an
        /// unresolved peer keeps its URI for a later attempt.
        /// </summary>
        public TimeSpan PeerDiscoveryTimeout { get; init; } = DefaultTimeout;

        /// <summary>
        /// Default for <see cref="MaxRedundantServers"/>.
        /// </summary>
        public const int DefaultMaxRedundantServers = 32;

        /// <summary>
        /// Default for <see cref="MaxConcurrentPeerDiscoveries"/>.
        /// </summary>
        public const int DefaultMaxConcurrentPeerDiscoveries = 4;

        /// <summary>
        /// The maximum number of members of a redundant Server set the client
        /// takes from <c>RedundantServerArray</c> and <c>ServerUriArray</c>,
        /// after removing duplicate ServerUris. Members of
        /// <c>RedundantServerArray</c> come first; further entries are
        /// ignored. Range 1–256. Default <see cref="DefaultMaxRedundantServers"/>.
        /// </summary>
        public int MaxRedundantServers { get; init; } = DefaultMaxRedundantServers;

        /// <summary>
        /// The maximum number of members whose endpoints are discovered at the
        /// same time. <see cref="PeerDiscoveryTimeout"/> applies to each member
        /// from the moment its discovery starts. Range 1–32. Default
        /// <see cref="DefaultMaxConcurrentPeerDiscoveries"/>.
        /// </summary>
        public int MaxConcurrentPeerDiscoveries { get; init; } = DefaultMaxConcurrentPeerDiscoveries;

        /// <summary>
        /// Maximum concurrent short-lived peer sessions used for probing. Range 1–32; defaults to four.
        /// </summary>
        public int MaxConcurrentPeerProbes { get; init; } = 4;

        /// <summary>
        /// A separate identity for reading peer ServiceLevel and ServerStatus.State. Configure
        /// it with only the required read permissions on each peer. Never supply the active
        /// session identity. The default uses anonymous and does not fall back to the active user.
        /// </summary>
        public IUserIdentity? ProbeIdentity { get; init; }

        /// <summary>
        /// Independently provisioned application URI of the first server (the shared URI for a
        /// transparent set). Required when enabling managed server redundancy or failover token
        /// reuse, even with a fully populated endpoint or a cached handoff. Do not copy it from
        /// unauthenticated discovery. The URI is verified against the authenticated server certificate.
        /// </summary>
        public string? ExpectedServerApplicationUri { get; init; }

        /// <summary>
        /// When <see langword="true"/>, a <see cref="ManagedSession"/> reads <c>Server.ServiceLevel</c> and
        /// <c>Server.ServerStatus.State</c> of every other member of a non-transparent redundant Server set
        /// over a short session on a certificate-authenticated UA SecureChannel, when a selection or
        /// failover decision needs them: when first connecting to a non-Healthy server, for a failover,
        /// and during ServiceLevel
        /// monitoring once the Server in use is no longer Healthy. A member whose resolved endpoint does not
        /// keep the security mode and policy of the session in use, or whose channel certificate does not
        /// identify that member, is not probed. The values replace those
        /// reported by the Server in use, so the initial connect, the
        /// failover target selection and the failover decision compare the members' current
        /// <c>ServiceLevel</c> (OPC 10000-4 §6.6.2.4.5). A failed probe leaves the member's level unknown
        /// and records the failure in <see cref="RedundantServer.ResolutionError"/>.
        /// Each probe is bounded by <see cref="PeerDiscoveryTimeout"/>. Default <see langword="true"/>.
        /// </summary>
        public bool ProbeMemberServiceLevel { get; init; } = true;

        /// <summary>
        /// When set, a <see cref="ManagedSession"/> reads <c>Server.ServiceLevel</c> of the Server in use at
        /// this interval while it is connected to a non-transparent redundant Server set. When the
        /// <c>ServiceLevel</c> leaves the Healthy subrange (OPC 10000-4 §6.6.2.4.2) the redundancy snapshot is
        /// refreshed, including the member probe when <see cref="ProbeMemberServiceLevel"/> is set, and a
        /// failover decision that warrants it starts <see cref="ManagedSession.FailoverAsync"/> to the target
        /// the redundancy handler selects. Defaults to <see cref="DefaultMonitoringInterval"/>.
        /// Set to <see langword="null"/> to disable ongoing monitoring.
        /// </summary>
        public TimeSpan? ServiceLevelMonitoringInterval { get; init; } = DefaultMonitoringInterval;

        /// <summary>
        /// When set, the other members of a non-transparent redundant Server set are reached over the
        /// reverse connections (OPC 10000-6 §7.1.3) they open to this source, by their ServerUri, instead of
        /// dialing them. A member's endpoint is resolved with <c>GetEndpoints</c> over one of its connections
        /// (see <see cref="ReverseConnectRedundantServerEndpointResolver"/>), the member probe of
        /// <see cref="ProbeMemberServiceLevel"/> runs over one of its connections, and a failover to the
        /// member waits for its next connection. These waits use the endpoint url the member's connections
        /// announced, also when the member is not holding a connection at the moment. A failover to a member
        /// other than the Server first connected to and the later recovery on that member take its
        /// connections from this source, also when the first Server is reached over another
        /// <see cref="ManagedSessionOptions.ReverseConnectionSource"/> or a <see cref="ReverseConnectManager"/>,
        /// which the first Server keeps. A member that the source has not reported with an available
        /// connection, or whose probe waited for a connection in vain and that has none available now, carries
        /// <see cref="StatusCodes.BadNotConnected"/> in <see cref="RedundantServer.ResolutionError"/> and no
        /// <see cref="RedundantServer.Endpoint"/>, so it is never selected as a failover target. Default
        /// <see langword="null"/>: members are dialed.
        /// </summary>
        public IReverseConnectionAvailability? MemberConnections { get; init; }

        /// <summary>
        /// Validates that the bounds and the monitoring interval are usable.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when a configured timeout, member limit, or concurrency limit is out of range.
        /// </exception>
        public void Validate()
        {
            ValidateTimeout(RefreshTimeout, nameof(RefreshTimeout));
            ValidateTimeout(PeerDiscoveryTimeout, nameof(PeerDiscoveryTimeout));
            ValidateCount(MaxRedundantServers, 256, nameof(MaxRedundantServers));
            ValidateCount(MaxConcurrentPeerDiscoveries, 32, nameof(MaxConcurrentPeerDiscoveries));
            ValidateCount(MaxConcurrentPeerProbes, 32, nameof(MaxConcurrentPeerProbes));
            if (ExpectedServerApplicationUri != null &&
                !Uri.TryCreate(ExpectedServerApplicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "The expected server application URI must be absolute.", nameof(ExpectedServerApplicationUri));
            }
            if (ServiceLevelMonitoringInterval is TimeSpan interval)
            {
                ValidateTimeout(interval, nameof(ServiceLevelMonitoringInterval));
            }
        }

        private static void ValidateTimeout(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromMilliseconds(int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(name, value, "A redundancy timeout must be positive and finite.");
            }
        }

        private static void ValidateCount(int value, int maximum, string name)
        {
            if (value < 1 || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name, value, $"The limit must be between 1 and {maximum}.");
            }
        }
    }
}
