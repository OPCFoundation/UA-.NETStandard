// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace PadimSample
{
    /// <summary>
    /// Refreshes the device providers until host shutdown.
    /// </summary>
    internal sealed class DemonstrationRefresh : BackgroundService
    {
        /// <summary>
        /// Creates the refresh worker over the shared provider.
        /// </summary>
        public DemonstrationRefresh(DemonstrationDevice device)
        {
            m_device = device;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await m_device.RefreshAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }

        /// <summary>The application-owned deterministic provider.</summary>
        private readonly DemonstrationDevice m_device;
    }
}
