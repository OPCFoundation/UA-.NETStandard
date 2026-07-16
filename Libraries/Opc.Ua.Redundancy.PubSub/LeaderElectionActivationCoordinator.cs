/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
