// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Server startup task that drives the live <c>Server.ServiceLevel</c>
    /// node from an <see cref="IServiceLevelProvider"/>. It publishes provider
    /// changes and refreshes the built-in leadership policy once per second.
    /// </summary>
    public sealed class ServiceLevelStartupTask : IServerStartupTask, IDisposable
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="serviceLevelProvider">The service-level source.</param>
        public ServiceLevelStartupTask(IServiceLevelProvider serviceLevelProvider)
            : this(serviceLevelProvider, null)
        {
        }

        /// <summary>
        /// Creates the task with a time source for periodic reevaluation.
        /// </summary>
        /// <param name="serviceLevelProvider">The service-level source.</param>
        /// <param name="timeProvider">The time source, or null to use the server's clock.</param>
        public ServiceLevelStartupTask(IServiceLevelProvider serviceLevelProvider, TimeProvider? timeProvider)
        {
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
            m_leaderPolicy = serviceLevelProvider as LeaderServiceLevelProvider;
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (server.ServerObject?.ServiceLevel is not { } serviceLevel)
            {
                return default;
            }

            lock (m_publishLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServiceLevelStartupTask));
                }

                ServerSystemContext context = server.DefaultSystemContext;
                m_writeLevel = server is IServerServiceLevelControl control
                    ? control.ClaimServiceLevelControl()
                    : level =>
                    {
                        serviceLevel.Value = level;
                        serviceLevel.ClearChangeMasks(context, false);
                    };
                m_server = server;
                m_stoppingToken = cancellationToken;
                m_leaderPolicy?.SetServerContext(server);
                m_serviceLevelProvider.ServiceLevelChanged += Publish;
                if (m_leaderPolicy == null)
                {
                    Publish(m_serviceLevelProvider.GetServiceLevel());
                    return default;
                }
                m_logger = context?.Telemetry.CreateLogger<ServiceLevelStartupTask>();
            }

            Refresh();
            lock (m_publishLock)
            {
                if (!m_disposed && !cancellationToken.IsCancellationRequested)
                {
                    TimeProvider timeProvider = m_timeProvider ??
                        (server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System;
                    m_timer = timeProvider.CreateTimer(_ => RefreshOnTick(), null, s_refreshInterval, s_refreshInterval);
                }
            }
            return default;
        }

        /// <summary>
        /// Stops refreshing the policy and publishing its changes.
        /// </summary>
        public void Dispose()
        {
            lock (m_publishLock)
            {
                Volatile.Write(ref m_disposed, true);
                m_serviceLevelProvider.ServiceLevelChanged -= Publish;
                m_timer?.Dispose();
                m_timer = null;
                m_leaderPolicy?.SetServerContext(null);
                m_writeLevel = null;
                m_server = null;
            }
        }

        private void Refresh()
        {
            m_leaderPolicy!.RefreshServiceLevel();
            Publish(m_leaderPolicy.PublishedServiceLevel);
        }

        private void RefreshOnTick()
        {
            if (m_stoppingToken.IsCancellationRequested)
            {
                Dispose();
                return;
            }
            if (Volatile.Read(ref m_disposed) || Interlocked.Exchange(ref m_refreshing, 1) != 0)
            {
                return;
            }

            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                m_logger?.ServiceLevelRefreshFailed(ex);
            }
            finally
            {
                Volatile.Write(ref m_refreshing, 0);
            }
        }

        private void Publish(byte level)
        {
            lock (m_publishLock)
            {
                // An election or admin callback can arrive after a newer policy evaluation.
                PublishCore(m_leaderPolicy?.PublishedServiceLevel ?? level);
            }
        }

        private void PublishCore(byte level)
        {
            if (m_writeLevel == null || m_stoppingToken.IsCancellationRequested)
            {
                return;
            }
            if (LeaderServiceLevelProvider.HasIncompatibleIdentity(m_server))
            {
                level = ServiceLevels.Maintenance;
            }
            if (m_leaderPolicy == null || m_lastLevel != level)
            {
                m_writeLevel(level);
                m_lastLevel = level;
            }
        }

        private static readonly TimeSpan s_refreshInterval = TimeSpan.FromSeconds(1);
        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly LeaderServiceLevelProvider? m_leaderPolicy;
        private readonly TimeProvider? m_timeProvider;
        private readonly Lock m_publishLock = new();
        private IServerContext? m_server;
        private Action<byte>? m_writeLevel;
        private ILogger? m_logger;
        private CancellationToken m_stoppingToken;
        private ITimer? m_timer;
        private byte? m_lastLevel;
        private int m_refreshing;
        private bool m_disposed;
    }

    /// <summary>
    /// Log messages for periodic service-level evaluation.
    /// </summary>
    internal static partial class ServiceLevelStartupTaskLog
    {
        /// <summary>
        /// Logs an unsuccessful refresh of the current service level.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.ServiceLevelStartupTask,
            Level = LogLevel.Warning,
            Message = "Could not publish the refreshed server service level.")]
        public static partial void ServiceLevelRefreshFailed(this ILogger logger, Exception exception);
    }
}
