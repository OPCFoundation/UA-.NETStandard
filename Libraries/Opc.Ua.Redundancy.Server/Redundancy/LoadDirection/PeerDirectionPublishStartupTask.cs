/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Server startup task that publishes the local Server's direction signals to the shared store: the health
    /// <c>ServiceLevel</c> immediately whenever it changes, and the load weight coalesced to at most one write per
    /// <see cref="LoadDirectionOptions.LoadPublishInterval"/>. Peers read these through an
    /// <see cref="IPeerDirectionView"/> to direct Clients to the best Server.
    /// </summary>
    public sealed class PeerDirectionPublishStartupTask : IServerStartupTask, IDisposable
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
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
            m_loadWeightProvider = loadWeightProvider ?? throw new ArgumentNullException(nameof(loadWeightProvider));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            string? localServerUri = ResolveLocalServerUri(server);
            m_logger = server.Telemetry.CreateLogger<PeerDirectionPublishStartupTask>();
            if (string.IsNullOrEmpty(localServerUri))
            {
                m_logger.LogWarning(
                    "Load direction disabled: the local ServerUri is unavailable, so peers cannot be directed to this Server.");
                return;
            }

            m_publisher = new SharedPeerDirectionPublisher(
                m_store, server.MessageContext, m_protector, m_options, m_timeProvider, localServerUri!);

            // Publish the current values once, then react to changes.
            await PublishServiceLevelSafeAsync(
                m_serviceLevelProvider.GetServiceLevel(), cancellationToken).ConfigureAwait(false);

            byte initialLoad = m_loadWeightProvider.GetLoadWeight();
            Volatile.Write(ref m_latestLoad, initialLoad);
            m_lastPublishedLoad = initialLoad;
            await PublishLoadSafeAsync(initialLoad, cancellationToken).ConfigureAwait(false);

            m_serviceLevelProvider.ServiceLevelChanged += OnServiceLevelChanged;
            m_loadWeightProvider.LoadWeightChanged += OnLoadWeightChanged;

            if (m_options.LoadPublishInterval > TimeSpan.Zero)
            {
                m_loadTimer = new Timer(
                    OnLoadTick, null, m_options.LoadPublishInterval, m_options.LoadPublishInterval);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_serviceLevelProvider.ServiceLevelChanged -= OnServiceLevelChanged;
            m_loadWeightProvider.LoadWeightChanged -= OnLoadWeightChanged;
            m_loadTimer?.Dispose();
            m_loadTimer = null;
        }

        private void OnServiceLevelChanged(byte serviceLevel)
        {
            _ = PublishServiceLevelSafeAsync(serviceLevel, CancellationToken.None);
        }

        private void OnLoadWeightChanged(byte loadWeight)
        {
            Volatile.Write(ref m_latestLoad, loadWeight);
        }

        private void OnLoadTick(object? state)
        {
            int latest = Volatile.Read(ref m_latestLoad);
            if (latest == m_lastPublishedLoad)
            {
                return;
            }
            m_lastPublishedLoad = latest;
            _ = PublishLoadSafeAsync((byte)latest, CancellationToken.None);
        }

        private async Task PublishServiceLevelSafeAsync(byte serviceLevel, CancellationToken cancellationToken)
        {
            IPeerDirectionPublisher? publisher = m_publisher;
            if (publisher == null)
            {
                return;
            }
            try
            {
                await publisher.PublishServiceLevelAsync(serviceLevel, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.LogDebug(ex, "Failed to publish the health ServiceLevel direction signal.");
            }
        }

        private async Task PublishLoadSafeAsync(byte loadWeight, CancellationToken cancellationToken)
        {
            IPeerDirectionPublisher? publisher = m_publisher;
            if (publisher == null)
            {
                return;
            }
            try
            {
                await publisher.PublishLoadWeightAsync(loadWeight, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.LogDebug(ex, "Failed to publish the load-weight direction signal.");
            }
        }

        private static string? ResolveLocalServerUri(IServerInternal server)
        {
            string[] serverUris = server.ServerUris.ToArray();
            return serverUris.Length > 0 ? serverUris[0] : null;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly ILoadWeightProvider m_loadWeightProvider;
        private readonly TimeProvider m_timeProvider;
        private ILogger? m_logger;
        private IPeerDirectionPublisher? m_publisher;
        private Timer? m_loadTimer;
        private int m_latestLoad;
        private int m_lastPublishedLoad;
    }
}
