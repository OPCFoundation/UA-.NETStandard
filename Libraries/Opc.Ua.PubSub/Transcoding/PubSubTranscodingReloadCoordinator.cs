/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Watches <see cref="PubSubTranscodingOptions"/> through an
    /// <see cref="IOptionsMonitor{TOptions}"/> and reconciles the set of
    /// running transcoding bridges on every change, reconfiguring only the
    /// routes whose declarative content actually changed. Added routes are
    /// started, removed routes are disposed, and unchanged routes keep
    /// running undisturbed.
    /// </summary>
    internal sealed class PubSubTranscodingReloadCoordinator : IAsyncDisposable
    {
        private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(250);

        private readonly IServiceProvider m_serviceProvider;
        private readonly IOptionsMonitor<PubSubTranscodingOptions> m_options;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_reloadLock = new(1, 1);
        private readonly Lock m_gate = new();

        private readonly Dictionary<string, ActiveRoute> m_active
            = new(StringComparer.Ordinal);

        private TranscodingBridgeActivator? m_activator;
        private IDisposable? m_changeSubscription;
        private CancellationTokenSource? m_debounce;
        private bool m_started;
        private bool m_disposed;

        public PubSubTranscodingReloadCoordinator(
            IServiceProvider serviceProvider,
            IOptionsMonitor<PubSubTranscodingOptions> options,
            ITelemetryContext telemetry)
        {
            m_serviceProvider = serviceProvider
                ?? throw new ArgumentNullException(nameof(serviceProvider));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_logger = telemetry.CreateLogger<PubSubTranscodingReloadCoordinator>();
        }

        /// <summary>
        /// Applies the current configuration and starts watching for changes.
        /// Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                if (m_disposed || m_started)
                {
                    return;
                }
                m_started = true;
                m_activator = new TranscodingBridgeActivator(m_serviceProvider);
                m_changeSubscription = m_options.OnChange((_, _) => ScheduleReload());
            }

            await ApplyAsync(m_options.CurrentValue, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reconciles the running bridges against the current configuration
        /// immediately (bypassing the change debounce).
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public ValueTask ReloadNowAsync(CancellationToken cancellationToken = default)
        {
            return ApplyAsync(m_options.CurrentValue, cancellationToken);
        }

        /// <summary>
        /// Number of currently active routes. Test hook.
        /// </summary>
        internal int ActiveRouteCount => m_active.Count;

        /// <summary>
        /// Returns the active bridge for a route, or <see langword="null"/>.
        /// Test hook used to assert that unchanged routes are not rebuilt.
        /// </summary>
        internal PubSubTranscodingBridge? GetActiveBridge(string name)
        {
            return m_active.TryGetValue(name, out ActiveRoute route) ? route.Bridge : null;
        }

        private void ScheduleReload()
        {
            CancellationTokenSource debounce;
            lock (m_gate)
            {
                if (m_disposed || !m_started)
                {
                    return;
                }
                m_debounce?.Cancel();
                m_debounce = new CancellationTokenSource();
                debounce = m_debounce;
            }

            _ = DebounceAndReloadAsync(debounce);
        }

        private async Task DebounceAndReloadAsync(CancellationTokenSource debounce)
        {
            try
            {
                await Task.Delay(s_debounceInterval, debounce.Token).ConfigureAwait(false);
                await ApplyAsync(m_options.CurrentValue, debounce.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "PubSub transcoding hot reload failed.");
            }
            finally
            {
                lock (m_gate)
                {
                    if (ReferenceEquals(m_debounce, debounce))
                    {
                        m_debounce = null;
                    }
                }
                debounce.Dispose();
            }
        }

        private async ValueTask ApplyAsync(
            PubSubTranscodingOptions options,
            CancellationToken cancellationToken)
        {
            await m_reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TranscodingBridgeActivator? activator;
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    activator = m_activator;
                }
                if (activator is null)
                {
                    return;
                }

                var desired = new Dictionary<string, TranscodeRouteOptions>(
                    StringComparer.Ordinal);
                if (options.Routes is not null)
                {
                    foreach (TranscodeRouteOptions route in options.Routes)
                    {
                        if (string.IsNullOrEmpty(route.Name))
                        {
                            m_logger.LogWarning(
                                "Ignoring a transcoding route with no Name.");
                            continue;
                        }
                        desired[route.Name!] = route;
                    }
                }

                var toRemove = new List<string>();
                foreach (KeyValuePair<string, ActiveRoute> entry in m_active)
                {
                    if (!desired.ContainsKey(entry.Key))
                    {
                        toRemove.Add(entry.Key);
                    }
                }
                foreach (string name in toRemove)
                {
                    await DisposeRouteAsync(name).ConfigureAwait(false);
                }

                foreach (KeyValuePair<string, TranscodeRouteOptions> entry in desired)
                {
                    string name = entry.Key;
                    string signature = TranscodeRouteOptionsFactory.ComputeSignature(entry.Value);
                    if (m_active.TryGetValue(name, out ActiveRoute existing))
                    {
                        if (string.Equals(existing.Signature, signature, StringComparison.Ordinal))
                        {
                            continue;
                        }
                        await DisposeRouteAsync(name).ConfigureAwait(false);
                    }
                    try
                    {
                        TranscodingBridgeDescriptor descriptor =
                            TranscodeRouteOptionsFactory.Create(entry.Value);
                        PubSubTranscodingBridge bridge = activator.Create(descriptor);
                        bridge.Start();
                        m_active[name] = new ActiveRoute(signature, bridge);
                        m_logger.LogInformation(
                            "Transcoding route '{Route}' configured.", name);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex,
                            "Failed to configure transcoding route '{Route}'.", name);
                    }
                }
            }
            finally
            {
                m_reloadLock.Release();
            }
        }

        private async ValueTask DisposeRouteAsync(string name)
        {
            if (m_active.TryGetValue(name, out ActiveRoute route))
            {
                m_active.Remove(name);
                await route.Bridge.DisposeAsync().ConfigureAwait(false);
                m_logger.LogInformation("Transcoding route '{Route}' removed.", name);
            }
        }

        public async ValueTask DisposeAsync()
        {
            IDisposable? subscription;
            CancellationTokenSource? debounce;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                subscription = m_changeSubscription;
                m_changeSubscription = null;
                debounce = m_debounce;
                m_debounce = null;
            }

            debounce?.Cancel();
            debounce?.Dispose();
            subscription?.Dispose();

            await m_reloadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (ActiveRoute route in m_active.Values)
                {
                    await route.Bridge.DisposeAsync().ConfigureAwait(false);
                }
                m_active.Clear();
            }
            finally
            {
                m_reloadLock.Release();
            }
            m_reloadLock.Dispose();
        }

        private readonly record struct ActiveRoute(
            string Signature,
            PubSubTranscodingBridge Bridge);
    }
}
