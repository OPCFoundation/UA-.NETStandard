// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Hosted service that starts the configuration-driven transcoding
    /// reload coordinator once the PubSub application is available and tears
    /// it down on shutdown.
    /// </summary>
    internal sealed class PubSubTranscodingReloadHostedService : IHostedService
    {
        private readonly PubSubTranscodingReloadCoordinator m_coordinator;

        public PubSubTranscodingReloadHostedService(
            PubSubTranscodingReloadCoordinator coordinator)
        {
            m_coordinator = coordinator
                ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return m_coordinator.StartAsync(cancellationToken).AsTask();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return m_coordinator.DisposeAsync().AsTask();
        }
    }
}
