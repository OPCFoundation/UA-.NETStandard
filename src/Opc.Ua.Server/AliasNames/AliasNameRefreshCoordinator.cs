/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Owns one refresh worker and one coalesced pending signal for an alias materialization host.
    /// </summary>
    internal sealed class AliasNameRefreshCoordinator : IDisposable, IAsyncDisposable
    {
        public AliasNameRefreshCoordinator(
            string owner,
            ITelemetryContext telemetry,
            Func<long, CancellationToken, ValueTask> refresh)
        {
            m_refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                .CreateLogger<AliasNameRefreshCoordinator>();
            m_background = new BackgroundTaskScope(owner, telemetry,
                maxConcurrency: 1, drainTimeout: TimeSpan.FromSeconds(5));
            m_background.Run("RefreshAliasNodes", RunAsync);
        }

        public int PendingTaskCount => m_background.PendingCount;

        public int PendingRefreshCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_pending ? 1 : 0;
                }
            }
        }

        public void RequestRefresh()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_generation = unchecked(m_generation + 1);
                if (!m_pending)
                {
                    m_pending = true;
                    m_signal.Release();
                }
            }
        }

        public bool IsCurrent(long generation)
        {
            lock (m_lock)
            {
                return !m_disposed && m_generation == generation;
            }
        }

        public void Dispose()
        {
            lock (m_lock)
            {
                m_disposed = true;
                m_pending = false;
            }
            m_background.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
            await m_background.DisposeAsync().ConfigureAwait(false);
            m_signal.Dispose();
        }

        private async ValueTask RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                await m_signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                long generation;
                lock (m_lock)
                {
                    generation = m_generation;
                    m_pending = false;
                }
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await m_refresh(generation, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (
                    ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException &&
                    (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
                {
                    m_logger.AliasRefreshFailed(ex, generation);
                }
            }
        }

        private readonly Func<long, CancellationToken, ValueTask> m_refresh;
        private readonly BackgroundTaskScope m_background;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_signal = new(0, 1);
        private readonly Lock m_lock = new();
        private long m_generation;
        private bool m_pending;
        private bool m_disposed;
    }

    /// <summary>
    /// Records refresh failures without disguising an unavailable store as an empty snapshot.
    /// </summary>
    internal static partial class AliasNameRefreshCoordinatorLog
    {
        [LoggerMessage(EventId = ServerEventIds.AliasNameNodeManager + 5, Level = LogLevel.Error,
            Message = "Alias refresh generation {Generation} failed. " +
                "The next store change will retry reconciliation.")]
        public static partial void AliasRefreshFailed(this ILogger logger, Exception exception, long generation);
    }
}
