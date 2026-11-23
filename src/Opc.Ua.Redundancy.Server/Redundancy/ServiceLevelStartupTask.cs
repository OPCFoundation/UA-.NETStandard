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
    /// node from an <see cref="IServiceLevelProvider"/>: it sets the initial
    /// value and updates the node when the effective level changes. It samples
    /// the provider once per second as health and load inputs need not raise
    /// change notifications.
    /// </summary>
    public sealed class ServiceLevelStartupTask : IServerStartupTask, IServiceLevelController, IDisposable
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="serviceLevelProvider">The service-level source.</param>
        public ServiceLevelStartupTask(IServiceLevelProvider serviceLevelProvider)
            : this(serviceLevelProvider, TimeProvider.System)
        {
        }

        /// <summary>
        /// Creates the task with a clock for periodic health and load sampling.
        /// </summary>
        internal ServiceLevelStartupTask(IServiceLevelProvider serviceLevelProvider, TimeProvider timeProvider)
        {
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ServerObjectState? serverObject = server.ServerObject;
            if (serverObject?.ServiceLevel == null)
            {
                return default;
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                if (m_server != null)
                {
                    throw new InvalidOperationException("ServiceLevel publication has already started.");
                }
                ServerSystemContext context = server.DefaultSystemContext;
                m_logger = context.Telemetry.CreateLogger<ServiceLevelStartupTask>();
                m_publishLevel = server is IServerServiceLevelControl control
                    ? control.ClaimServiceLevelControl()
                    : level =>
                    {
                        serverObject.ServiceLevel.Value = level;
                        serverObject.ServiceLevel.ClearChangeMasks(context, false);
                    };
                m_server = server;
                m_serviceLevelProvider.ServiceLevelChanged += OnServiceLevelChanged;
                try
                {
                    Refresh();
                    TimeSpan interval = TimeSpan.FromSeconds(1);
                    m_timer = m_timeProvider.CreateTimer(_ => RefreshFromTimer(), null, interval, interval);
                    m_stopRegistration = cancellationToken.Register(Stop);
                }
                catch
                {
                    Stop();
                    throw;
                }
            }
            return default;
        }

        /// <summary>
        /// Caps the advertised level. Maximum removes the cap without overriding the provider.
        /// </summary>
        public void SetServiceLevel(byte serviceLevel)
        {
            lock (m_lock)
            {
                m_adminCap = serviceLevel;
                Refresh();
            }
        }

        /// <summary>
        /// Stops sampling and detaches the provider callback.
        /// </summary>
        public void Dispose()
        {
            Stop();
            m_stopRegistration.Dispose();
        }

        private void Stop()
        {
            lock (m_lock)
            {
                m_timer?.Dispose();
                m_timer = null;
                m_serviceLevelProvider.ServiceLevelChanged -= OnServiceLevelChanged;
                if (m_server is IServerServiceLevelControlRevocation control && m_publishLevel != null)
                {
                    control.ReleaseServiceLevelControl(m_publishLevel);
                }
                m_publishLevel = null;
                m_server = null;
                m_lastPublished = null;
            }
        }

        private void OnServiceLevelChanged(byte _)
        {
            lock (m_lock)
            {
                Refresh();
            }
        }

        private void RefreshFromTimer()
        {
            lock (m_lock)
            {
                try
                {
                    Refresh();
                }
                catch (Exception ex)
                {
                    m_logger?.ServiceLevelRefreshFailed(ex);
                }
            }
        }

        private void Refresh()
        {
            if (m_publishLevel == null)
            {
                return;
            }

            byte level = m_adminCap == ServiceLevels.Maintenance || m_server is INodeIdFactoryProvider
                { NodeIdFactory: ReplicaNodeIdFactory { IsCompatible: false } }
                    ? ServiceLevels.Maintenance
                    : Math.Min(m_adminCap, m_serviceLevelProvider.GetServiceLevel());
            if (m_lastPublished != level)
            {
                m_publishLevel(level);
                m_lastPublished = level;
            }
        }

        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_lock = new();
        private ILogger? m_logger;
        private IServerContext? m_server;
        private Action<byte>? m_publishLevel;
        private ITimer? m_timer;
        private CancellationTokenRegistration m_stopRegistration;
        private byte m_adminCap = ServiceLevels.Maximum;
        private byte? m_lastPublished;
    }

    /// <summary>
    /// Logging for service-level publication.
    /// </summary>
    internal static partial class ServiceLevelStartupTaskLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.ServiceLevelStartupTask, Level = LogLevel.Warning,
            Message = "Could not refresh the server ServiceLevel.")]
        public static partial void ServiceLevelRefreshFailed(this ILogger logger, Exception exception);
    }
}
