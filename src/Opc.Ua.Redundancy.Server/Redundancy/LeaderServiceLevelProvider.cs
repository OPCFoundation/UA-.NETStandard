// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Combines the administrative override, replica readiness, role, health and load
    /// into a <c>ServiceLevel</c> according to OPC 10000-4 §6.6.2.4.2.
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
            : this(election, failoverMode, getConnectedClientCount, getHealthServiceLevel, null)
        {
        }

        /// <summary>
        /// Creates a provider that also checks whether the elected role is ready to serve.
        /// </summary>
        internal LeaderServiceLevelProvider(
            ILeaderElection election,
            RedundancySupport failoverMode,
            Func<uint>? getConnectedClientCount,
            Func<byte>? getHealthServiceLevel,
            Func<bool, bool>? isRoleReady)
        {
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_failoverMode = failoverMode;
            m_getConnectedClientCount = getConnectedClientCount;
            m_getHealthServiceLevel = getHealthServiceLevel;
            m_isRoleReady = isRoleReady;
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

        /// <summary>
        /// Gets the last evaluated value used by the hosted publisher.
        /// </summary>
        internal byte PublishedServiceLevel => (byte)Volatile.Read(ref m_publishedLevel);

        /// <inheritdoc/>
        public byte GetServiceLevel()
        {
            IServerContext? server = Volatile.Read(ref m_server);
            try
            {
                return CalculateServiceLevel(server);
            }
            catch (Exception ex)
            {
                server?.DefaultSystemContext?.Telemetry.CreateLogger<LeaderServiceLevelProvider>()
                    .ServiceLevelInputsUnavailable(ex);
                return ServiceLevels.NoData;
            }
        }

        /// <summary>
        /// Sets a service-level override that remains in effect across elections.
        /// An incompatible replica identity still advertises Maintenance.
        /// </summary>
        /// <param name="serviceLevel">The level to advertise until the override is cleared.</param>
        public void SetServiceLevel(byte serviceLevel)
        {
            SetOverride(serviceLevel);
        }

        /// <summary>
        /// Clears an administrative override and immediately recomputes the role, health and load.
        /// </summary>
        public void ClearServiceLevelOverride()
        {
            SetOverride(kNoManualServiceLevel);
        }

        /// <summary>
        /// Stops following the leader election.
        /// </summary>
        public void Dispose()
        {
            m_election.LeadershipChanged -= OnLeadershipChanged;
        }

        /// <summary>
        /// Connects or disconnects the default load and identity inputs for the hosted server.
        /// </summary>
        internal void SetServerContext(IServerContext? server)
        {
            Volatile.Write(ref m_server, server);
        }

        /// <summary>
        /// Requires the event feed to catch up under the acquired writer grant before advertising availability.
        /// </summary>
        internal void SetEventReadiness(Func<bool> isReady)
        {
            Volatile.Write(ref m_isEventReady, isReady);
        }

        /// <summary>
        /// Updates the announced policy result after health or load may have changed.
        /// </summary>
        internal void RefreshServiceLevel()
        {
            byte level;
            lock (m_policyLock)
            {
                level = GetServiceLevel();
                if (level == m_publishedLevel)
                {
                    return;
                }
                Volatile.Write(ref m_publishedLevel, level);
            }
            ServiceLevelChanged?.Invoke(level);
        }

        /// <summary>
        /// Checks the local namespace and NodeId compatibility gate.
        /// </summary>
        internal static bool HasIncompatibleIdentity(IServerContext? server)
        {
            return server is INodeIdFactoryProvider { NodeIdFactory: ReplicaNodeIdFactory { IsCompatible: false } };
        }

        private byte CalculateServiceLevel(IServerContext? server)
        {
            if (HasIncompatibleIdentity(server))
            {
                return ServiceLevels.Maintenance;
            }

            int manualServiceLevel = Volatile.Read(ref m_manualServiceLevel);
            if (manualServiceLevel != ServiceLevels.Maintenance &&
                Volatile.Read(ref m_isEventReady)?.Invoke() == false)
            {
                return ServiceLevels.NoData;
            }
            if (manualServiceLevel >= 0)
            {
                return (byte)manualServiceLevel;
            }

            bool isLeader = m_election.IsLeader;
            if (m_isRoleReady?.Invoke(isLeader) == false)
            {
                return ServiceLevels.NoData;
            }

            byte level = isLeader ? m_leaderLevel : GetStandbyServiceLevel();
            if (m_getHealthServiceLevel != null)
            {
                level = Math.Min(level, m_getHealthServiceLevel());
            }

            return ApplyHealthyLoadBalancing(server, level);
        }

        private void SetOverride(int serviceLevel)
        {
            Volatile.Write(ref m_manualServiceLevel, serviceLevel);
            RefreshServiceLevel();
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            RefreshServiceLevel();
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

        private byte ApplyHealthyLoadBalancing(IServerContext? server, byte level)
        {
            if (level <= ServiceLevels.HealthyMinimum)
            {
                return level;
            }

            uint clientCount = m_getConnectedClientCount?.Invoke() ??
                (uint)((server as IServerInternal)?.SessionManager?.GetSessions().Count ?? 0);
            byte maximumDecrement = (byte)(level - ServiceLevels.HealthyMinimum);
            byte decrement = clientCount > maximumDecrement ? maximumDecrement : (byte)clientCount;
            return (byte)(level - decrement);
        }

        private readonly Lock m_policyLock = new();
        private readonly ILeaderElection m_election;
        private readonly RedundancySupport m_failoverMode;
        private readonly Func<uint>? m_getConnectedClientCount;
        private readonly Func<byte>? m_getHealthServiceLevel;
        private readonly Func<bool, bool>? m_isRoleReady;
        private Func<bool>? m_isEventReady;
        private readonly byte m_leaderLevel = ServiceLevels.Maximum;
        private readonly byte? m_standbyLevel;
        private IServerContext? m_server;
        private int m_manualServiceLevel = kNoManualServiceLevel;
        private int m_publishedLevel = -1;

        private const int kNoManualServiceLevel = -1;
    }

    /// <summary>
    /// Log messages for service-level policy evaluation.
    /// </summary>
    internal static partial class LeaderServiceLevelProviderLog
    {
        /// <summary>
        /// Logs that a policy input could not be collected and NoData is being used.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.LeaderServiceLevelProvider,
            Level = LogLevel.Warning,
            Message = "Could not evaluate the server service-level policy; using NoData.")]
        public static partial void ServiceLevelInputsUnavailable(this ILogger logger, Exception exception);
    }
}
