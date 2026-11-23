// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Exchanges the effective server status through the protected shared store.
    /// </summary>
    internal sealed class PeerStatusStartupTask(
        ServerRedundancyController controller,
        ISharedKeyValueStore? store,
        IRecordProtector? protector,
        TimeProvider timeProvider) : IServerStartupTask, IDisposable, IAsyncDisposable
    {
        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (store == null || protector == null)
            {
                return default;
            }

            if (controller.Options.PeerStatusUpdateInterval < TimeSpan.FromMilliseconds(1) ||
                controller.Options.PeerStatusUpdateInterval > TimeSpan.FromMilliseconds(int.MaxValue) ||
                controller.Options.PeerStatusLifetime <= controller.Options.PeerStatusUpdateInterval)
            {
                throw new InvalidOperationException(
                    "PeerStatusUpdateInterval must be positive and supported by timers, " +
                    "and PeerStatusLifetime must exceed it.");
            }

            lock (m_lifetimeLock)
            {
                m_stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                m_poll = PollAsync(server, m_stop.Token);
            }
            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lifetimeLock)
            {
                try
                {
                    m_stop?.Cancel();
                }
                finally
                {
                    m_stop?.Dispose();
                    m_stop = null;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Dispose();
            await m_poll.ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes the currently configured entries once, publishing the local status even if
        /// its identifier is not listed in this server's peer array.
        /// </summary>
        internal async ValueTask RefreshAsync(
            IServerContext server,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            RedundancySupport mode = controller.Mode;
            bool transparent = mode == RedundancySupport.Transparent;
            string? localId = transparent
                ? controller.Options.CurrentServerId
                : server.DefaultSystemContext.ServerUris.GetString(0);
            string keyPrefix = transparent ? TransparentKeyPrefix : KeyPrefix;
            TimeSpan interval = controller.Options.PeerStatusUpdateInterval;
            using CancellationTokenSource timeout = timeProvider.CreateCancellationTokenSource(interval);
            using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var local = new PeerStatus(
                server.CurrentState,
                server.ServerObject?.ServiceLevel?.Value ?? 0,
                timeProvider.GetUtcNow().Ticks);
            Task publish = string.IsNullOrEmpty(localId)
                ? Task.CompletedTask
                : PublishAsync(keyPrefix + localId, local, logger, exchange.Token);
            var observations = new Dictionary<string, PeerStatus>(StringComparer.Ordinal);
            ArrayOf<string> peerIds = controller.Options.GetPeerIds(mode);
            for (int i = 0; i < peerIds.Count; i++)
            {
                string id = peerIds[i];
                PeerStatus? status = id == localId
                    ? local
                    : await ReadAsync(keyPrefix + id, logger, exchange.Token).ConfigureAwait(false);
                if (status is { } observed)
                {
                    observations.Add(id, observed);
                }
            }
            await publish.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var peers = new Dictionary<string, (ServerState State, byte ServiceLevel)>(StringComparer.Ordinal);
            long now = timeProvider.GetUtcNow().Ticks;
            foreach ((string id, PeerStatus status) in observations)
            {
                long age = now - status.Timestamp;
                // Allow up to one update interval of clock skew, but reject implausibly future records.
                if (IsKnownState(status.State) &&
                    age >= -interval.Ticks && age < controller.Options.PeerStatusLifetime.Ticks)
                {
                    peers.Add(id, (status.State, status.ServiceLevel));
                }
            }
            await controller.UpdatePeerStatusAsync(mode, peers, cancellationToken).ConfigureAwait(false);
        }

        private async Task PollAsync(IServerContext server, CancellationToken ct)
        {
            ILogger logger = server.DefaultSystemContext.Telemetry.CreateLogger<PeerStatusStartupTask>();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (controller.Mode != RedundancySupport.None)
                    {
                        await RefreshAsync(server, logger, ct).ConfigureAwait(false);
                    }
                    await timeProvider.Delay(controller.Options.PeerStatusUpdateInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Host shutdown.
            }
        }

        private async Task PublishAsync(string key, PeerStatus status, ILogger logger, CancellationToken ct)
        {
            try
            {
                ByteString record = protector!.Protect(RecordProtectionContext.Create("peer-status", key),
                    Encode(status));
                await store!.SetAsync(key, record, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    logger.PeerStatusStoreFailed(ex);
                }
            }
        }

        private async ValueTask<PeerStatus?> ReadAsync(string key, ILogger logger, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                (bool found, ByteString record) = await store!.TryGetAsync(key, ct).ConfigureAwait(false);
                return found && protector!.TryUnprotect(RecordProtectionContext.Create("peer-status", key),
                            record, out ByteString payload)
                    ? Decode(payload)
                    : null;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    logger.PeerStatusStoreFailed(ex);
                }
                return null;
            }
        }

        private static ByteString Encode(PeerStatus status)
        {
            byte[] bytes = new byte[RecordSize];
            bytes[0] = RecordVersion;
            bytes[1] = status.ServiceLevel;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), (int)status.State);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(6), status.Timestamp);
            return new ByteString(bytes);
        }

        private static PeerStatus? Decode(ByteString payload)
        {
            ReadOnlySpan<byte> bytes = payload.Span;
            if (bytes.Length != RecordSize || bytes[0] != RecordVersion)
            {
                return null;
            }

            var state = (ServerState)BinaryPrimitives.ReadInt32LittleEndian(bytes[2..]);
            long timestamp = BinaryPrimitives.ReadInt64LittleEndian(bytes[6..]);
            return IsKnownState(state) && timestamp > 0 && timestamp <= DateTime.MaxValue.Ticks
                ? new PeerStatus(state, bytes[1], timestamp)
                : null;
        }

        private static bool IsKnownState(ServerState state)
        {
            return state is ServerState.Running or ServerState.Failed or ServerState.NoConfiguration or
                ServerState.Suspended or ServerState.Shutdown or ServerState.Test or ServerState.CommunicationFault;
        }

        /// <summary>
        /// The key prefix for peer status records, followed by the application URI.
        /// </summary>
        internal const string KeyPrefix = "peer-status/application/";

        /// <summary>
        /// The distinct key prefix for Transparent peer status records, followed by CurrentServerId.
        /// </summary>
        internal const string TransparentKeyPrefix = "peer-status/transparent/";

        private const byte RecordVersion = 1;
        private const int RecordSize = 14;
        private readonly Lock m_lifetimeLock = new();
        private CancellationTokenSource? m_stop;
        private Task m_poll = Task.CompletedTask;

        private readonly record struct PeerStatus(ServerState State, byte ServiceLevel, long Timestamp);
    }

    /// <summary>
    /// Source-generated log messages for peer status exchange.
    /// </summary>
    internal static partial class PeerStatusStartupTaskLog
    {
        /// <summary>
        /// Logs a failed status store operation.
        /// </summary>
        [LoggerMessage(EventId = RedundancyServerEventIds.PeerStatusStartupTask + 0,
            Level = LogLevel.Debug, Message = "Peer status store operation failed.")]
        public static partial void PeerStatusStoreFailed(this ILogger logger, Exception exception);
    }
}
