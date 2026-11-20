// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: drives an <see cref="IPeerDiscovery"/> refresh loop and applies each
    /// discovered peer set to the redundant server set (published through <c>FindServers</c>) and, when present,
    /// to the CRDT gossip fabric. The peer set therefore tracks scale-up and scale-down at runtime without a
    /// restart.
    /// </summary>
    internal sealed class PeerDiscoveryStartupTask : IServerStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Creates a startup task that refreshes discovered peers and updates the redundant server set.
        /// </summary>
        /// <param name="discovery">The peer discovery source to refresh.</param>
        /// <param name="provider">The provider that publishes discovered redundant servers.</param>
        /// <param name="options">The peer discovery refresh options.</param>
        /// <param name="gossipSink">Optional gossip sink that receives discovered gossip endpoints.</param>
        public PeerDiscoveryStartupTask(
            IPeerDiscovery discovery,
            DiscoveredRedundantServerSetProvider provider,
            PeerDiscoveryOptions options,
            IGossipPeerSink? gossipSink = null)
        {
            m_discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_gossipSink = gossipSink;
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            m_logger = server.DefaultSystemContext.Telemetry.CreateLogger<PeerDiscoveryStartupTask>();
            m_discovery.PeersChanged += OnPeersChanged;
            m_loop = Task.Run(() => RefreshLoopAsync(m_cts.Token), CancellationToken.None);
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_discovery.PeersChanged -= OnPeersChanged;
            m_cts.Cancel();
            if (m_loop != null)
            {
                try
                {
                    await m_loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }
            m_cts.Dispose();
        }

        private void OnPeersChanged(ArrayOf<DiscoveredPeer> peers)
        {
            m_provider.Update(peers);

            if (m_gossipSink != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    DiscoveredPeer peer = peers[i];
                    if (peer?.GossipEndpoint != null)
                    {
                        m_gossipSink.AddPeer(peer.GossipEndpoint);
                    }
                }
            }
        }

        private async Task RefreshLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await m_discovery.RefreshAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    m_logger?.PeerDiscoveryRefreshFailed(ex);
                }

                try
                {
                    await Task.Delay(m_options.RefreshInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private readonly IPeerDiscovery m_discovery;
        private readonly DiscoveredRedundantServerSetProvider m_provider;
        private readonly PeerDiscoveryOptions m_options;
        private readonly IGossipPeerSink? m_gossipSink;
        private readonly CancellationTokenSource m_cts = new();
        private ILogger? m_logger;
        private Task? m_loop;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PeerDiscoveryStartupTask"/>.
    /// </summary>
    internal static partial class PeerDiscoveryStartupTaskLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.PeerDiscoveryStartupTask + 0, Level = LogLevel.Warning,
            Message = "Peer discovery refresh failed; retrying after the interval.")]
        public static partial void PeerDiscoveryRefreshFailed(this ILogger logger, Exception exception);
    }

}
