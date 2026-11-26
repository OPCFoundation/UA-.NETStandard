// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vision.VisualInspectionCell
{
    internal sealed class InspectionJobSeeder : IHostedService
    {
        public InspectionJobSeeder(
            InspectionJobControlProvider provider,
            ILogger<InspectionJobSeeder> logger)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await m_provider.SeedAsync(cancellationToken).ConfigureAwait(false);
            m_logger.JobsSeeded(InspectionJobControlProvider.InspectionOrderId,
                InspectionJobControlProvider.ReworkRejectOrderId);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private readonly InspectionJobControlProvider m_provider;
        private readonly ILogger<InspectionJobSeeder> m_logger;
    }

    internal static partial class InspectionJobSeederLog
    {
        [LoggerMessage(EventId = VisualInspectionCellEventIds.Isa95 + 1,
            Level = LogLevel.Information,
            Message = "Seeded ISA-95 V2 orders {InspectionOrderId} and {ReworkRejectOrderId}.")]
        public static partial void JobsSeeded(
            this ILogger<InspectionJobSeeder> logger,
            string inspectionOrderId,
            string reworkRejectOrderId);
    }
}
