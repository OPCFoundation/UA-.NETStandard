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
    /// Server startup task that materializes the live
    /// <c>Server.ServerRedundancy</c> node as the generated subtype for the
    /// configured <see cref="ServerRedundancyOptions"/> via the
    /// <see cref="ServerRedundancyController"/>.
    /// </summary>
    public sealed class ServerRedundancyStartupTask : IServerStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="controller">The redundancy controller.</param>
        /// <param name="warnIfServiceLevelProviderMissing">
        /// Whether to warn when non-transparent redundancy has no registered service-level provider.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="controller"/> is <c>null</c>.</exception>
        public ServerRedundancyStartupTask(
            ServerRedundancyController controller,
            bool warnIfServiceLevelProviderMissing = false)
            : this(controller, warnIfServiceLevelProviderMissing, TimeProvider.System)
        {
        }

        /// <summary>
        /// Creates the startup task and supplies the time source for status refresh.
        /// </summary>
        internal ServerRedundancyStartupTask(
            ServerRedundancyController controller,
            bool warnIfServiceLevelProviderMissing,
            TimeProvider timeProvider)
        {
            m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
            m_warnIfServiceLevelProviderMissing = warnIfServiceLevelProviderMissing;
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            WarnIfServiceLevelProviderMissing(server);

            await m_controller.AttachAsync(server, cancellationToken).ConfigureAwait(false);
            m_refresh = RefreshPeerStatusAsync(server, m_stop.Token);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            try
            {
                m_stop.Cancel();
                if (m_refresh != null)
                {
                    await m_refresh.ConfigureAwait(false);
                }
            }
            finally
            {
                m_stop.Dispose();
            }
        }

        private async Task RefreshPeerStatusAsync(IServerContext server, CancellationToken ct)
        {
            ILogger? logger = null;
            try
            {
                while (true)
                {
                    await m_timeProvider.Delay(s_refreshInterval, ct).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(s_refreshInterval);
                    try
                    {
                        await m_controller.RefreshPeerStatusAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        logger ??= server.DefaultSystemContext.Telemetry.CreateLogger<ServerRedundancyStartupTask>();
                        logger.PeerStatusRefreshFailed(ex);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The startup task is being disposed.
            }
        }

        private void WarnIfServiceLevelProviderMissing(IServerContext server)
        {
            if (!m_warnIfServiceLevelProviderMissing || !m_controller.Options.IsNonTransparentMode)
            {
                return;
            }

            ILogger logger = server.DefaultSystemContext.Telemetry.CreateLogger<ServerRedundancyStartupTask>();
            logger.NonTransparentServerRedundancyModeWithoutServiceLevelProvider(m_controller.Mode);
        }

        private readonly ServerRedundancyController m_controller;
        private readonly bool m_warnIfServiceLevelProviderMissing;
        private readonly TimeProvider m_timeProvider;
        private readonly CancellationTokenSource m_stop = new();
        private Task? m_refresh;
        private int m_disposed;
        private static readonly TimeSpan s_refreshInterval = TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ServerRedundancyStartupTask"/>.
    /// </summary>
    internal static partial class ServerRedundancyStartupTaskLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.ServerRedundancyStartupTask + 0,
            Level = LogLevel.Warning,
            Message = "Non-transparent server redundancy mode {RedundancyMode} is configured without a registered " +
                "IServiceLevelProvider. Register AddServerServiceLevel(...) or an IServiceLevelProvider so " +
                "Server.ServiceLevel reflects failover health.")]
        public static partial void NonTransparentServerRedundancyModeWithoutServiceLevelProvider(
            this ILogger logger,
            RedundancySupport redundancyMode);

        [LoggerMessage(EventId = RedundancyServerEventIds.ServerRedundancyStartupTask + 1,
            Level = LogLevel.Warning,
            Message = "Could not refresh authenticated redundant peer status. Retrying after the refresh interval.")]
        public static partial void PeerStatusRefreshFailed(this ILogger logger, Exception exception);
    }

}
