// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Publishes the local server's health and endpoints at startup and before they expire, independently of
    /// discovery requests. Health changes also trigger publication; load changes remain coalesced.
    /// </summary>
    public sealed class PeerDirectionPublishStartupTask : IServerStartupTask, IDisposable, IAsyncDisposable
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
            m_protector = LoadDirectionRecordValidation.RequireProtection(m_store, protector);
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
            if (localServerUri is not { Length: > 0 })
            {
                m_logger.LoadDirectionDisabledLocalServerUriUnavailableForPeerDirection();
                return;
            }

            m_publisher = new SharedPeerDirectionPublisher(
                m_store, server.MessageContext, m_protector, m_options, m_timeProvider, localServerUri);
            if (server is IServerEndpointRegistryProvider registry)
            {
                m_endpointRegistry = registry;
                m_endpointPublisher = new SharedPeerEndpointPublisher(
                    m_store, server.MessageContext, m_protector, m_options, localServerUri, m_timeProvider);
            }

            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            m_lifetime = lifetime;
            CancellationToken ct = lifetime.Token;
            m_serviceLevelProvider.ServiceLevelChanged += OnServiceLevelChanged;
            await PublishServiceLevelSafeAsync(ct).ConfigureAwait(false);
            await PublishEndpointsSafeAsync(localServerUri, ct).ConfigureAwait(false);
            await PublishLoadSafeAsync(ct).ConfigureAwait(false);

            if (m_options.StalenessWindow > TimeSpan.Zero)
            {
                long ticks = Math.Max(TimeSpan.TicksPerMillisecond,
                    Math.Min(m_options.StalenessWindow.Ticks / 2, TimeSpan.TicksPerDay));
                TimeSpan interval = TimeSpan.FromTicks(ticks);
                m_refreshTimer = m_timeProvider.CreateTimer(OnRefreshTick, null, interval, interval);
            }

            if (m_options.LoadPublishInterval > TimeSpan.Zero)
            {
                m_loadTimer = m_timeProvider.CreateTimer(
                    OnLoadTick, null, m_options.LoadPublishInterval, m_options.LoadPublishInterval);
            }
            m_publishLoop = PublishPendingAsync(localServerUri, ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _ = StopPublishingAsync();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return new ValueTask(StopPublishingAsync());
        }

        private async Task StopPublishingAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_serviceLevelProvider.ServiceLevelChanged -= OnServiceLevelChanged;
            m_loadTimer?.Dispose();
            m_refreshTimer?.Dispose();
            m_lifetime?.Cancel();
            m_pending.Writer.TryComplete();
            try
            {
                if (m_publishLoop is Task loop)
                {
                    await loop.ConfigureAwait(false);
                }
            }
            finally
            {
                m_lifetime?.Dispose();
            }
        }

        private void OnServiceLevelChanged(byte serviceLevel)
        {
            Volatile.Write(ref m_healthPending, 1);
            m_pending.Writer.TryWrite(true);
        }

        private void OnLoadTick(object? state)
        {
            Volatile.Write(ref m_loadPending, 1);
            m_pending.Writer.TryWrite(true);
        }

        private void OnRefreshTick(object? state)
        {
            Volatile.Write(ref m_refreshPending, 1);
            m_pending.Writer.TryWrite(true);
        }

        private async Task PublishPendingAsync(string localServerUri, CancellationToken ct)
        {
            try
            {
                await foreach (bool _ in m_pending.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    bool refresh = Interlocked.Exchange(ref m_refreshPending, 0) != 0;
                    if (Interlocked.Exchange(ref m_healthPending, 0) != 0 || refresh)
                    {
                        await PublishServiceLevelSafeAsync(ct).ConfigureAwait(false);
                    }
                    if (refresh)
                    {
                        await PublishEndpointsSafeAsync(localServerUri, ct).ConfigureAwait(false);
                    }
                    if (Interlocked.Exchange(ref m_loadPending, 0) != 0)
                    {
                        await PublishLoadSafeAsync(ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The server or publisher stopped.
            }
        }

        private async Task PublishServiceLevelSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await m_publisher!.PublishServiceLevelAsync(m_serviceLevelProvider.GetServiceLevel(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                m_logger?.FailedToPublishHealthServiceLevelDirectionSignal(ex);
            }
        }

        private async Task PublishEndpointsSafeAsync(string localServerUri, CancellationToken cancellationToken)
        {
            try
            {
                if (m_endpointRegistry is not null &&
                    m_endpointPublisher is SharedPeerEndpointPublisher publisher)
                {
                    ArrayOf<EndpointDescription> endpoints = m_endpointRegistry.ServerEndpoints;
                    if (LoadDirectionRecordValidation.MatchesMember(endpoints, localServerUri))
                    {
                        await publisher.PublishAsync(endpoints, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                m_logger?.FailedToPublishLocalEndpointSnapshot(ex);
            }
        }

        private async Task PublishLoadSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                byte load = m_loadWeightProvider.GetLoadWeight();
                if (load != m_lastPublishedLoad)
                {
                    await m_publisher!.PublishLoadWeightAsync(load, cancellationToken).ConfigureAwait(false);
                    m_lastPublishedLoad = load;
                }
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
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
        private readonly Channel<bool> m_pending = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        private SharedPeerDirectionPublisher? m_publisher;
        private IServerEndpointRegistryProvider? m_endpointRegistry;
        private SharedPeerEndpointPublisher? m_endpointPublisher;
        private CancellationTokenSource? m_lifetime;
        private Task? m_publishLoop;
        private ITimer? m_loadTimer;
        private ITimer? m_refreshTimer;
        private byte? m_lastPublishedLoad;
        private int m_healthPending;
        private int m_loadPending;
        private int m_refreshPending;
        private int m_disposed;
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

        [LoggerMessage(EventId = RedundancyServerEventIds.PeerDirectionPublishStartupTask + 3,
            Level = LogLevel.Debug,
            Message = "Failed to publish the local endpoint snapshot for load direction.")]
        public static partial void FailedToPublishLocalEndpointSnapshot(this ILogger logger, Exception exception);
    }

}
