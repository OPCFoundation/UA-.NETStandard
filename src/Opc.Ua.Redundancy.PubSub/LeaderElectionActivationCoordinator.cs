// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Activates every PubSub component on the current leader and keeps every
    /// component standby on follower instances.
    /// </summary>
    public sealed class LeaderElectionActivationCoordinator : IPubSubActivationCoordinator, IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="LeaderElectionActivationCoordinator"/>.
        /// </summary>
        /// <param name="election">Leader election provider for this PubSub instance.</param>
        /// <param name="telemetry">Telemetry context for logging.</param>
        public LeaderElectionActivationCoordinator(ILeaderElection election, ITelemetryContext? telemetry = null)
        {
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_logger = telemetry.CreateLogger<LeaderElectionActivationCoordinator>();
        }

        /// <inheritdoc/>
        public event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged;

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            bool start;
            lock (m_gate)
            {
                start = !m_started;
                if (start)
                {
                    m_election.LeadershipChanged += OnLeadershipChanged;
                    m_started = true;
                }
            }

            if (start)
            {
                m_election.Start();
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                if (m_started)
                {
                    m_election.LeadershipChanged -= OnLeadershipChanged;
                    m_started = false;
                }
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubComponentRole> GetRoleAsync(
            string componentId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId is required.", nameof(componentId));
            }

            lock (m_gate)
            {
                m_componentIds.Add(componentId);
            }

            PubSubComponentRole role = m_election.IsLeader
                ? PubSubComponentRole.Active
                : PubSubComponentRole.Standby;
            return new ValueTask<PubSubComponentRole>(role);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                if (m_started)
                {
                    m_election.LeadershipChanged -= OnLeadershipChanged;
                    m_started = false;
                }
            }

            return default;
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            string[] componentIds;
            lock (m_gate)
            {
                componentIds = [.. m_componentIds];
            }

            PubSubComponentRole role = isLeader
                ? PubSubComponentRole.Active
                : PubSubComponentRole.Standby;
            m_logger.PubSubLeaderElectionLeadershipChanged(isLeader);

            foreach (string componentId in componentIds)
            {
                RoleChanged?.Invoke(this, new PubSubRoleChangedEventArgs(componentId, role));
            }
        }

        private readonly ILeaderElection m_election;
        private readonly ILogger m_logger;
        private readonly Lock m_gate = new();
        private readonly HashSet<string> m_componentIds = new(StringComparer.Ordinal);
        private bool m_started;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="LeaderElectionActivationCoordinator"/>.
    /// </summary>
    internal static partial class LeaderElectionActivationCoordinatorLog
    {
        [LoggerMessage(EventId = RedundancyPubSubEventIds.LeaderElectionActivationCoordinator + 0,
            Level = LogLevel.Debug,
            Message = "PubSub leader election leadership changed: {IsLeader}.")]
        public static partial void PubSubLeaderElectionLeadershipChanged(this ILogger logger, bool isLeader);
    }

}
