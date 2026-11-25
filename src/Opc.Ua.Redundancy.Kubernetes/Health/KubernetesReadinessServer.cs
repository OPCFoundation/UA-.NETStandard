// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: small <see cref="HttpListener"/> readiness and liveness endpoint driven by
    /// OPC UA <c>ServiceLevel</c>.
    /// </summary>
    public sealed class KubernetesReadinessServer : IAsyncDisposable
    {
        /// <summary>
        /// Creates a Kubernetes readiness server.
        /// </summary>
        /// <param name="serviceLevelProvider">The ServiceLevel source.</param>
        /// <param name="options">Readiness endpoint options.</param>
        /// <param name="logger">Optional logger.</param>
        public KubernetesReadinessServer(
            IServiceLevelProvider serviceLevelProvider,
            KubernetesReadinessOptions options,
            ILogger? logger = null)
        {
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_logger = logger;
            m_prefixes =
            [
                ToPrefix(m_options.Host, m_options.Port, m_options.ReadinessPath),
                ToPrefix(m_options.Host, m_options.Port, m_options.LivenessPath)
            ];
        }

        /// <summary>
        /// Starts the HTTP listener.
        /// </summary>
        /// <remarks>
        /// The listener is created here rather than in the constructor: the managed
        /// <see cref="HttpListener"/> binds its port when a listener that has prefixes
        /// is closed, so a server that was built but never started would otherwise
        /// bind - and fail with "address already in use" - on disposal.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The server was disposed.</exception>
        public void Start()
        {
            lock (m_lock)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (m_listener != null)
                {
                    return;
                }
                var listener = new HttpListener();
                foreach (string prefix in m_prefixes)
                {
                    listener.Prefixes.Add(prefix);
                }
                listener.Start();
                m_listener = listener;
                m_loop = Task.Run(() => ListenAsync(listener, m_cts.Token));
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            HttpListener? listener;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                listener = m_listener;
            }

            m_cts.Cancel();
            listener?.Close();
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

            await DrainRequestHandlersAsync().ConfigureAwait(false);

            m_handlerSlots.Dispose();
            m_cts.Dispose();
        }

        /// <summary>
        /// Determines whether the current OPC UA ServiceLevel satisfies the configured readiness threshold.
        /// </summary>
        /// <returns><c>true</c> when the service is ready for traffic; otherwise, <c>false</c>.</returns>
        internal bool IsReady()
        {
            return IsReady(m_serviceLevelProvider.GetServiceLevel(), m_options.ReadyMinimumServiceLevel);
        }

        /// <summary>
        /// Determines whether a ServiceLevel satisfies the readiness threshold.
        /// </summary>
        /// <param name="serviceLevel">The current OPC UA ServiceLevel value.</param>
        /// <param name="readyMinimumServiceLevel">The minimum ServiceLevel that reports readiness.</param>
        /// <returns><c>true</c> when <paramref name="serviceLevel"/> is high enough; otherwise, <c>false</c>.</returns>
        internal static bool IsReady(byte serviceLevel, byte readyMinimumServiceLevel)
        {
            return serviceLevel >= readyMinimumServiceLevel;
        }

        private async Task ListenAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }

                // Bound the fan-out: an unauthenticated readiness endpoint would
                // otherwise spawn one unowned handler per inbound request, with
                // nothing to stop a flood and nothing to wait for at shutdown.
                try
                {
                    await m_handlerSlots.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    context.Response.Close();
                    return;
                }

                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await HandleAsync(context).ConfigureAwait(false);
                        }
                        finally
                        {
                            m_handlerSlots.Release();
                        }
                    },
                    CancellationToken.None);
            }
        }

        /// <summary>
        /// Waits for the request handlers still in flight to finish by reclaiming
        /// every concurrency slot.
        /// </summary>
        /// <remarks>
        /// Holding all <see cref="kMaxConcurrentRequests"/> slots is only possible
        /// once no handler owns one, so this doubles as the drain. It is bounded so a
        /// wedged client socket cannot stall disposal indefinitely; the listener is
        /// already closed by then, so an abandoned handler can only fail its own
        /// response write.
        /// </remarks>
        private async Task DrainRequestHandlersAsync()
        {
            using var drainTimeout = new CancellationTokenSource(kDrainTimeout);
            try
            {
                for (int i = 0; i < kMaxConcurrentRequests; i++)
                {
                    await m_handlerSlots.WaitAsync(drainTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                m_logger?.KubernetesReadinessDrainTimedOut();
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? string.Empty;
                bool isReadyPath = MatchesProbePath(path, m_options.ReadinessPath);
                bool isLivePath = MatchesProbePath(path, m_options.LivenessPath);
                HttpStatusCode status = isReadyPath
                    ? (IsReady() ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
                    : (isLivePath ? HttpStatusCode.OK : HttpStatusCode.NotFound);
                context.Response.StatusCode = (int)status;
                string body = status switch
                {
                    HttpStatusCode.OK => "ok",
                    HttpStatusCode.ServiceUnavailable => "not ready",
                    _ => "not found"
                };
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger?.KubernetesReadinessRequestFailed(ex);
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static bool MatchesProbePath(string requestPath, string configuredPath)
        {
            string path = configuredPath.StartsWith('/', StringComparison.Ordinal) ? configuredPath : "/" + configuredPath;
            return string.Equals(requestPath.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static string ToPrefix(string host, int port, string path)
        {
            string normalizedPath = path.StartsWith('/', StringComparison.Ordinal) ? path : "/" + path;
            if (!normalizedPath.EndsWith('/', StringComparison.Ordinal))
            {
                normalizedPath += "/";
            }
            return string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}{normalizedPath}");
        }

        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly KubernetesReadinessOptions m_options;
        private readonly ILogger? m_logger;
        private readonly string[] m_prefixes;
        private readonly Lock m_lock = new();
        private readonly CancellationTokenSource m_cts = new();
        private readonly SemaphoreSlim m_handlerSlots = new(kMaxConcurrentRequests, kMaxConcurrentRequests);
        private const int kMaxConcurrentRequests = 16;
        private static readonly TimeSpan kDrainTimeout = TimeSpan.FromSeconds(5);
        private HttpListener? m_listener;
        private Task? m_loop;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="KubernetesReadinessServer"/>.
    /// </summary>
    internal static partial class KubernetesReadinessServerLog
    {
        [LoggerMessage(EventId = RedundancyKubernetesEventIds.KubernetesReadinessServer + 0, Level = LogLevel.Error,
            Message = "Kubernetes readiness request failed.")]
        public static partial void KubernetesReadinessRequestFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = RedundancyKubernetesEventIds.KubernetesReadinessServer + 1, Level = LogLevel.Warning,
            Message = "Kubernetes readiness request handlers did not drain before the shutdown timeout.")]
        public static partial void KubernetesReadinessDrainTimedOut(this ILogger logger);
    }

}
