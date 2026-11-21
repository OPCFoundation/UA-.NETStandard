// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Default <see cref="IDiPostSetupRunner"/>: enumerates registered
    /// configurators, filters by manager type, invokes each one in
    /// registration order, and propagates the first exception.
    /// </summary>
    internal sealed class DiPostSetupRunner : IDiPostSetupRunner
    {
        /// <summary>
        /// Creates a runner over the configurators in their service registration order.
        /// </summary>
        public DiPostSetupRunner(
            IServiceProvider services,
            IEnumerable<IDiPostSetupConfigurator> configurators,
            ILogger<DiPostSetupRunner>? logger = null)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_configurators = configurators
                ?? throw new ArgumentNullException(nameof(configurators));
            m_logger = logger;
        }

        /// <summary>
        /// Awaits matching configurators and aborts startup with diagnostics on the first failure.
        /// </summary>
        public async ValueTask RunAsync(
            DiNodeManager manager,
            CancellationToken cancellationToken)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            var context = new DiPostSetupContext(manager, m_services, cancellationToken);
            Type managerType = manager.GetType();
            int index = 0;

            foreach (IDiPostSetupConfigurator configurator in m_configurators)
            {
                if (!configurator.TargetManagerType.IsAssignableFrom(managerType))
                {
                    index++;
                    continue;
                }

                try
                {
                    await configurator.RunAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger?.DiPostSetupConfiguratorFailed(
                        ex,
                        index,
                        configurator.TargetManagerType.FullName,
                        managerType.FullName);

                    // Fail-fast: abort startup with diagnostic context.
                    throw new InvalidOperationException(
                        $"DI post-setup configurator #{index} (target '{configurator.TargetManagerType.FullName}') " +
                        $"failed for manager '{managerType.FullName}'. See inner exception.",
                        ex);
                }

                index++;
            }
        }

        /// <summary>The services available to configuration callbacks.</summary>
        private readonly IServiceProvider m_services;

        /// <summary>The configurators in service registration order.</summary>
        private readonly IEnumerable<IDiPostSetupConfigurator> m_configurators;

        /// <summary>The optional startup diagnostic logger.</summary>
        private readonly ILogger<DiPostSetupRunner>? m_logger;
    }

    /// <summary>
    /// Source-generated diagnostics for DI post-setup configuration.
    /// </summary>
    internal static partial class DiPostSetupRunnerLog
    {
        /// <summary>
        /// Reports the index and manager types of a failed configurator.
        /// </summary>
        [LoggerMessage(EventId = DiServerEventIds.DiPostSetupRunner + 0, Level = LogLevel.Error,
            Message = "DI post-setup configurator #{Index} (target {TargetType}) failed for manager {ManagerType}.")]
        public static partial void DiPostSetupConfiguratorFailed(
            this ILogger logger,
            Exception ex,
            int index,
            string? targetType,
            string? managerType);
    }
}
