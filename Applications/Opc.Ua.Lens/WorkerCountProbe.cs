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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens;

/// <summary>
/// Probes the live worker-pool resize behaviour of the V2 subscription
/// engine. The default <see cref="Subscriptions.SubscriptionConfig"/>
/// produces a worker pool of 2 (the V2 manager defaults).  The probe then
/// applies a config with <c>MinPublishRequestCount = 8</c> and asserts that
/// <c>PublishWorkerCount</c> climbs to ≥ 8 within ~1 s — a guard against the
/// "setters do not signal the publish controller" regression.
/// </summary>
internal static class WorkerCountProbe
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine("== WorkerCount probe ==");
        Console.WriteLine($"   endpoint: {endpointUrl}");

        var conn = new ConnectionService(telemetry);
        await using (conn.ConfigureAwait(false))
        {
            await conn.ConnectAsync(new ConnectionOptions
            {
                EndpointUrl = endpointUrl,
                Engine = SubscriptionEngineKind.ChannelV2
            }, ct).ConfigureAwait(false);

            ISubscriptionAdapter a = conn.CreateAdapter();
            if (a is null)
            {
                Console.WriteLine("FAIL: no adapter after connect");
                return 1;
            }

            await a.ApplySubscriptionAsync(new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(1000),
                KeepAliveCount = 10,
                MinPublishRequestCount = 2,
                MaxPublishRequestCount = 15
            }, ct).ConfigureAwait(false);

            // Settle.
            await Task.Delay(500, ct).ConfigureAwait(false);
            int initial = a.PublishWorkerCount;
            Console.WriteLine($"  initial worker count (min=2, max=15):     {initial}");

            // Bump the floor to 8 — the publish controller MUST notice and grow
            // the pool.  Before the SubscriptionManager setter fix this would
            // remain at 2 indefinitely.
            await a.ApplySubscriptionAsync(new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(1000),
                KeepAliveCount = 10,
                MinPublishRequestCount = 8,
                MaxPublishRequestCount = 15
            }, ct).ConfigureAwait(false);

            // Poll for ≤ 1 s waiting for the controller to wake and resize.
            int after = initial;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1000)
            {
                after = a.PublishWorkerCount;
                if (after >= 8)
                {
                    break;
                }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }

            Console.WriteLine($"  after raising min to 8 (elapsed {sw.ElapsedMilliseconds} ms): {after}");

            if (after < 8)
            {
                Console.WriteLine("FAIL: publish-worker pool did not resize to the new min within 1 s.");
                return 1;
            }

            // And shrink: lower max to 4, expect pool to drop to ≤ 4.
            await a.ApplySubscriptionAsync(new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(1000),
                KeepAliveCount = 10,
                MinPublishRequestCount = 1,
                MaxPublishRequestCount = 4
            }, ct).ConfigureAwait(false);

            int shrunk = after;
            sw.Restart();
            while (sw.ElapsedMilliseconds < 1500)
            {
                shrunk = a.PublishWorkerCount;
                if (shrunk <= 4)
                {
                    break;
                }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            Console.WriteLine($"  after lowering max to 4 (elapsed {sw.ElapsedMilliseconds} ms): {shrunk}");

            if (shrunk > 4)
            {
                Console.WriteLine("FAIL: publish-worker pool did not shrink to the new max within 1.5 s.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("WORKER PROBE PASS");
            return 0;
        }
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.WorkerProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.WorkerProbe");
    }
}
