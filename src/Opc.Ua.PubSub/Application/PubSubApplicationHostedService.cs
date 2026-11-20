/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Generic-host adapter that drives an
    /// <see cref="IPubSubApplication"/> through its
    /// <see cref="IPubSubApplication.StartAsync"/> /
    /// <see cref="IPubSubApplication.StopAsync"/> lifecycle. Registered
    /// automatically by <c>AddPubSub</c>.
    /// </summary>
    /// <remarks>
    /// Wires the application into the
    /// <see href="https://learn.microsoft.com/dotnet/core/extensions/generic-host">
    /// .NET Generic Host</see> lifetime, mirroring
    /// <c>Opc.Ua.Server.Hosting.OpcUaServerHostedService</c> in
    /// Opc.Ua.Server.
    /// </remarks>
    public sealed class PubSubApplicationHostedService : IHostedService
    {
        private readonly IPubSubApplication m_application;
        private readonly ILogger<PubSubApplicationHostedService> m_logger;

        /// <summary>
        /// Initializes a new <see cref="PubSubApplicationHostedService"/>.
        /// </summary>
        /// <param name="application">The application to drive.</param>
        /// <param name="logger">Logger.</param>
        public PubSubApplicationHostedService(
            IPubSubApplication application,
            ILogger<PubSubApplicationHostedService> logger)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            m_application = application;
            m_logger = logger;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            m_logger.StartingPubSubApplication(m_application.ApplicationId);
            await m_application.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            m_logger.StoppingPubSubApplication(m_application.ApplicationId);
            await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PubSubApplicationHostedService"/>.
    /// </summary>
    internal static partial class PubSubApplicationHostedServiceLog
    {
        [LoggerMessage(EventId = PubSubEventIds.PubSubApplicationHostedService + 0, Level = LogLevel.Information,
            Message = "Starting PubSub application {Id}.")]
        public static partial void StartingPubSubApplication(this ILogger logger, string id);

        [LoggerMessage(EventId = PubSubEventIds.PubSubApplicationHostedService + 1, Level = LogLevel.Information,
            Message = "Stopping PubSub application {Id}.")]
        public static partial void StoppingPubSubApplication(this ILogger logger, string id);
    }

}
