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
            m_listener = new HttpListener();
            m_listener.Prefixes.Add(ToPrefix(m_options.Host, m_options.Port, m_options.ReadinessPath));
            m_listener.Prefixes.Add(ToPrefix(m_options.Host, m_options.Port, m_options.LivenessPath));
        }

        /// <summary>
        /// Starts the HTTP listener.
        /// </summary>
        public void Start()
        {
            lock (m_lock)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
                m_listener.Start();
                m_loop = Task.Run(() => ListenAsync(m_cts.Token));
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            m_cts.Cancel();
            m_listener.Close();
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

        internal bool IsReady()
        {
            return IsReady(m_serviceLevelProvider.GetServiceLevel(), m_options.ReadyMinimumServiceLevel);
        }

        internal static bool IsReady(byte serviceLevel, byte readyMinimumServiceLevel)
        {
            return serviceLevel >= readyMinimumServiceLevel;
        }

        private async Task ListenAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await m_listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context), ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? string.Empty;
                bool isReadyPath = string.Equals(path, m_options.ReadinessPath, StringComparison.OrdinalIgnoreCase);
                bool ok = !isReadyPath || IsReady();
                context.Response.StatusCode = ok ? (int)HttpStatusCode.OK : (int)HttpStatusCode.ServiceUnavailable;
                string body = ok ? "ok" : "not ready";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger?.LogError(ex, "Kubernetes readiness request failed.");
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static string ToPrefix(string host, int port, string path)
        {
            string normalizedPath = path.StartsWith('/') ? path : "/" + path;
            if (!normalizedPath.EndsWith('/'))
            {
                normalizedPath += "/";
            }
            return string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}{normalizedPath}");
        }

        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly KubernetesReadinessOptions m_options;
        private readonly ILogger? m_logger;
        private readonly HttpListener m_listener;
        private readonly Lock m_lock = new();
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_loop;
        private bool m_started;
        private bool m_disposed;
    }
}
