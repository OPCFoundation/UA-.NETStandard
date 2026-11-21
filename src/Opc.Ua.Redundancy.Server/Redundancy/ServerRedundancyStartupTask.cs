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
    public sealed class ServerRedundancyStartupTask : IServerStartupTask
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
        {
            m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
            m_warnIfServiceLevelProviderMissing = warnIfServiceLevelProviderMissing;
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
    }

}
