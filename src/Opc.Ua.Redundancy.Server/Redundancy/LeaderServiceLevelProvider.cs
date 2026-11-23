// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// An <see cref="IServiceLevelProvider"/> driven by leadership: the
    /// leader reports the highest level so Clients prefer it; standbys report
    /// a lower level. This is how non-transparent server Failover is advertised
    /// through the OPC 10000-4 §6.6.2.4.2 <c>ServiceLevel</c> mechanism.
    /// </summary>
    public sealed class LeaderServiceLevelProvider : IServiceLevelProvider, IServiceLevelController, IDisposable
    {
        /// <summary>
        /// Creates a leadership-driven service-level provider.
        /// </summary>
        /// <param name="election">The leader election to follow.</param>
        /// <param name="failoverMode">The configured redundancy failover mode.</param>
        /// <param name="getConnectedClientCount">Optional connected-client load function.</param>
        /// <param name="getHealthServiceLevel">Optional health-derived maximum service level.</param>
        public LeaderServiceLevelProvider(
            ILeaderElection election,
            RedundancySupport failoverMode = RedundancySupport.Warm,
            Func<uint>? getConnectedClientCount = null,
            Func<byte>? getHealthServiceLevel = null)
        {
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_failoverMode = failoverMode;
            m_getConnectedClientCount = getConnectedClientCount;
            m_getHealthServiceLevel = getHealthServiceLevel;
            m_election.LeadershipChanged += OnLeadershipChanged;
        }

        /// <summary>
        /// Creates a leadership-driven service-level provider with explicit
        /// levels. Use the mode-based overload for OPC 10000-4 subrange-aware
        /// defaults.
        /// </summary>
        /// <param name="election">The leader election to follow.</param>
        /// <param name="leaderLevel">Level reported while leader.</param>
        /// <param name="standbyLevel">Level reported while standby.</param>
        public LeaderServiceLevelProvider(
            ILeaderElection election,
            byte leaderLevel,
            byte standbyLevel)
            : this(election, RedundancySupport.Warm)
        {
            m_leaderLevel = leaderLevel;
            m_standbyLevel = standbyLevel;
        }

        /// <inheritdoc/>
        public event Action<byte>? ServiceLevelChanged;

        /// <inheritdoc/>
        public byte GetServiceLevel()
        {
            byte level = m_election.IsLeader ? m_leaderLevel : GetStandbyServiceLevel();
            level = Math.Min(level, (byte)Volatile.Read(ref m_manualServiceLevel));
            if (level != ServiceLevels.Maintenance && m_getHealthServiceLevel != null)
            {
                level = Math.Min(level, m_getHealthServiceLevel());
            }

            return ApplyHealthyLoadBalancing(level);
        }

        /// <summary>
        /// Sets a persistent administrative cap. Maximum removes the cap.
        /// </summary>
        public void SetServiceLevel(byte serviceLevel)
        {
            Volatile.Write(ref m_manualServiceLevel, serviceLevel);
            ServiceLevelChanged?.Invoke(GetServiceLevel());
        }

        /// <summary>
        /// Stops following the leader election.
        /// </summary>
        public void Dispose()
        {
            m_election.LeadershipChanged -= OnLeadershipChanged;
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            ServiceLevelChanged?.Invoke(GetServiceLevel());
        }

        private byte GetStandbyServiceLevel()
        {
            if (m_standbyLevel.HasValue)
            {
                return m_standbyLevel.GetValueOrDefault();
            }

            return m_failoverMode switch
            {
                RedundancySupport.Cold => ServiceLevels.NoData,
                RedundancySupport.Hot => ServiceLevels.Maximum,
                RedundancySupport.HotAndMirrored => ServiceLevels.Maximum,
                _ => ServiceLevels.DegradedMaximum
            };
        }

        private byte ApplyHealthyLoadBalancing(byte level)
        {
            if (m_getConnectedClientCount == null || !ServiceLevels.IsHealthy(level))
            {
                return level;
            }

            uint clientCount = m_getConnectedClientCount();
            byte maximumDecrement = (byte)(level - ServiceLevels.HealthyMinimum);
            byte decrement = clientCount > maximumDecrement ? maximumDecrement : (byte)clientCount;
            return (byte)(level - decrement);
        }

        private readonly ILeaderElection m_election;
        private readonly RedundancySupport m_failoverMode;
        private readonly Func<uint>? m_getConnectedClientCount;
        private readonly Func<byte>? m_getHealthServiceLevel;
        private readonly byte m_leaderLevel = ServiceLevels.Maximum;
        private readonly byte? m_standbyLevel;
        private int m_manualServiceLevel = ServiceLevels.Maximum;
    }
}
