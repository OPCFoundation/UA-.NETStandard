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
    /// Server startup task that publishes the local Server's direction signals to the shared store: the health
    /// <c>ServiceLevel</c> immediately whenever it changes, and the load weight coalesced to at most one write per
    /// <see cref="LoadDirectionOptions.LoadPublishInterval"/>. Peers read these through an
    /// <see cref="IPeerDirectionView"/> to direct Clients to the best Server.
    /// </summary>
    public sealed class PeerDirectionPublishStartupTask : IServerStartupTask, IDisposable
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="store">The shared store the signals are gossiped through.</param>
        /// <param name="protector">Protects record integrity.</param>
        /// <param name="options">The load-direction options.</param>
        /// <param name="serviceLevelProvider">The local health service-level source.</param>
        /// <param name="loadWeightProvider">The local load-weight source.</param>
        /// <param name="timeProvider">The time source for record timestamps.</param>
        public PeerDirectionPublishStartupTask(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            LoadDirectionOptions options,
            IServiceLevelProvider serviceLevelProvider,
            ILoadWeightProvider loadWeightProvider,
            TimeProvider timeProvider)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
            m_loadWeightProvider = loadWeightProvider ?? throw new ArgumentNullException(nameof(loadWeightProvider));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            string? localServerUri = ResolveLocalServerUri(server);
            m_logger = server.DefaultSystemContext.Telemetry.CreateLogger<PeerDirectionPublishStartupTask>();
            if (string.IsNullOrEmpty(localServerUri))
            {
                m_logger.LoadDirectionDisabledLocalServerUriUnavailableForPeerDirection();
                return;
            }

            m_publisher = new SharedPeerDirectionPublisher(
                m_store, server.MessageContext, m_protector, m_options, m_timeProvider, localServerUri!);

            // Publish the current values once, then react to changes.
            await PublishServiceLevelSafeAsync(
                m_serviceLevelProvider.GetServiceLevel(), cancellationToken).ConfigureAwait(false);

            byte initialLoad = m_loadWeightProvider.GetLoadWeight();
            Volatile.Write(ref m_latestLoad, initialLoad);
            m_lastPublishedLoad = initialLoad;
            await PublishLoadSafeAsync(initialLoad, cancellationToken).ConfigureAwait(false);

            m_serviceLevelProvider.ServiceLevelChanged += OnServiceLevelChanged;
            m_loadWeightProvider.LoadWeightChanged += OnLoadWeightChanged;

            if (m_options.LoadPublishInterval > TimeSpan.Zero)
            {
                m_loadTimer = new Timer(
                    OnLoadTick, null, m_options.LoadPublishInterval, m_options.LoadPublishInterval);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_serviceLevelProvider.ServiceLevelChanged -= OnServiceLevelChanged;
            m_loadWeightProvider.LoadWeightChanged -= OnLoadWeightChanged;
            m_loadTimer?.Dispose();
            m_loadTimer = null;
        }

        private void OnServiceLevelChanged(byte serviceLevel)
        {
            _ = PublishServiceLevelSafeAsync(serviceLevel, CancellationToken.None);
        }

        private void OnLoadWeightChanged(byte loadWeight)
        {
            Volatile.Write(ref m_latestLoad, loadWeight);
        }

        private void OnLoadTick(object? state)
        {
            int latest = Volatile.Read(ref m_latestLoad);
            if (latest == m_lastPublishedLoad)
            {
                return;
            }
            m_lastPublishedLoad = latest;
            _ = PublishLoadSafeAsync((byte)latest, CancellationToken.None);
        }

        private async Task PublishServiceLevelSafeAsync(byte serviceLevel, CancellationToken cancellationToken)
        {
            IPeerDirectionPublisher? publisher = m_publisher;
            if (publisher == null)
            {
                return;
            }
            try
            {
                await publisher.PublishServiceLevelAsync(serviceLevel, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.FailedToPublishHealthServiceLevelDirectionSignal(ex);
            }
        }

        private async Task PublishLoadSafeAsync(byte loadWeight, CancellationToken cancellationToken)
        {
            IPeerDirectionPublisher? publisher = m_publisher;
            if (publisher == null)
            {
                return;
            }
            try
            {
                await publisher.PublishLoadWeightAsync(loadWeight, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.FailedToPublishLoadWeightDirectionSignal(ex);
            }
        }

        private static string? ResolveLocalServerUri(IServerContext server)
        {
            string[] serverUris = server.DefaultSystemContext.ServerUris.ToArray();
            return serverUris.Length > 0 ? serverUris[0] : null;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly ILoadWeightProvider m_loadWeightProvider;
        private readonly TimeProvider m_timeProvider;
        private ILogger? m_logger;
        private IPeerDirectionPublisher? m_publisher;
        private Timer? m_loadTimer;
        private int m_latestLoad;
        private int m_lastPublishedLoad;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PeerDirectionPublishStartupTask"/>.
    /// </summary>
    internal static partial class PeerDirectionPublishStartupTaskLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.PeerDirectionPublishStartupTask + 0,
            Level = LogLevel.Warning,
            Message = "Load direction disabled: the local ServerUri is unavailable, so peers cannot be directed to " +
                "this Server.")]
        public static partial void LoadDirectionDisabledLocalServerUriUnavailableForPeerDirection(this ILogger logger);

        [LoggerMessage(EventId = RedundancyServerEventIds.PeerDirectionPublishStartupTask + 1,
            Level = LogLevel.Debug,
            Message = "Failed to publish the health ServiceLevel direction signal.")]
        public static partial void FailedToPublishHealthServiceLevelDirectionSignal(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.PeerDirectionPublishStartupTask + 2,
            Level = LogLevel.Debug,
            Message = "Failed to publish the load-weight direction signal.")]
        public static partial void FailedToPublishLoadWeightDirectionSignal(
            this ILogger logger,
            Exception exception);
    }

}
