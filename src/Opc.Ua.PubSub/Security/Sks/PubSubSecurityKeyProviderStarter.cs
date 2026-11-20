/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Generic-host adapter that starts the background key-refresh loop of
    /// every registered <see cref="PullSecurityKeyProvider"/> so subscribers
    /// begin pulling keys from their remote Security Key Service when the host
    /// starts. Registered by
    /// <c>AddPubSubSecurityKeyServiceClient</c>. Providers that do not pull
    /// (for example the push target) are left untouched.
    /// </summary>
    internal sealed class PubSubSecurityKeyProviderStarter : IHostedService
    {
        private readonly IEnumerable<IPubSubSecurityKeyProvider> m_providers;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes a new <see cref="PubSubSecurityKeyProviderStarter"/>.
        /// </summary>
        /// <param name="providers">All registered key providers.</param>
        /// <param name="telemetry">Telemetry context.</param>
        public PubSubSecurityKeyProviderStarter(
            IEnumerable<IPubSubSecurityKeyProvider> providers,
            ITelemetryContext telemetry)
        {
            m_providers = providers ?? throw new ArgumentNullException(nameof(providers));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<PubSubSecurityKeyProviderStarter>();
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (IPubSubSecurityKeyProvider provider in m_providers)
            {
                if (provider is not PullSecurityKeyProvider pullProvider)
                {
                    continue;
                }
                try
                {
                    await pullProvider.StartAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.InitialSksKeyPullFailed(ex, pullProvider.SecurityGroupId);
                }
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PubSubSecurityKeyProviderStarter"/>.
    /// </summary>
    internal static partial class PubSubSecurityKeyProviderStarterLog
    {
        [LoggerMessage(EventId = PubSubEventIds.PubSubSecurityKeyProviderStarter + 0, Level = LogLevel.Warning,
            Message = "Initial SKS key pull failed for SecurityGroupId {GroupId}; " +
                "the provider keeps serving cached keys and retries in the background.")]
        public static partial void InitialSksKeyPullFailed(
            this ILogger logger,
            Exception exception,
            string groupId);
    }

}
