// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Hosted service that materialises a configured transcoding bridge
    /// once the PubSub application is available: it resolves the source
    /// and target connections by name, builds the transcoder and egress,
    /// and registers the bridge on the source connection.
    /// </summary>
    internal sealed class PubSubTranscodingBridgeHostedService
        : IHostedService, IAsyncDisposable
    {
        private readonly IServiceProvider m_serviceProvider;
        private readonly TranscodingBridgeDescriptor m_descriptor;
        private PubSubTranscodingBridge? m_bridge;

        public PubSubTranscodingBridgeHostedService(
            IServiceProvider serviceProvider,
            TranscodingBridgeDescriptor descriptor)
        {
            m_serviceProvider = serviceProvider
                ?? throw new ArgumentNullException(nameof(serviceProvider));
            m_descriptor = descriptor
                ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var activator = new TranscodingBridgeActivator(m_serviceProvider);
            m_bridge = activator.Create(m_descriptor);
            m_bridge.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return DisposeAsync().AsTask();
        }

        public async ValueTask DisposeAsync()
        {
            if (m_bridge is not null)
            {
                await m_bridge.DisposeAsync().ConfigureAwait(false);
                m_bridge = null;
            }
        }
    }
}
