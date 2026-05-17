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
using UaLens.Views;

namespace UaLens;

/// <summary>
/// Headless validator for the Lines-mode per-item value buffer.
/// Subscribes to a numeric variable, runs for ~5 s, and asserts that:
/// <list type="bullet">
/// <item>the per-item sample count is &gt; 0,</item>
/// <item>the expand-only min/max envelope is populated and Min ≤ Max,</item>
/// <item>at least one envelope value is finite (sanity).</item>
/// </list>
/// </summary>
internal static class LinesProbe
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine("== Lines probe ==");
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
                Console.WriteLine("FAIL: no adapter");
                return 1;
            }

            await a.ApplySubscriptionAsync(new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(1000),
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                PublishingEnabled = true
            }, ct).ConfigureAwait(false);

            int id = await a.AddItemAsync(new MonitoredItemConfig
            {
                DisplayName = "value:UInt32",
                NodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_UInt32"),
                AttributeId = Attributes.Value,
                SamplingInterval = TimeSpan.FromMilliseconds(1000),
                QueueSize = 1,
                DiscardOldest = true,
                MonitoringMode = MonitoringMode.Reporting
            }, ct).ConfigureAwait(false);

            var canvas = new AnimationCanvas { Mode = AnimationMode.Lines };
            canvas.Bind(a.Events, a.Counters);

            const int seconds = 5;
            for (int i = 0; i < seconds; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                canvas.Drain();
                int n = canvas.LineSampleCountFor(id);
                (double, double)? r = canvas.LineRangeFor(id);
                Console.WriteLine($"  t={i + 1}s  samples={n}  range={(r.HasValue ? $"[{r.Value.Item1}, {r.Value.Item2}]" : "(none)")}");
            }

            int total = canvas.LineSampleCountFor(id);
            (double, double)? envelope = canvas.LineRangeFor(id);
            await a.RemoveItemAsync(id, ct).ConfigureAwait(false);

            Console.WriteLine($"  TOTAL samples : {total}");
            Console.WriteLine($"  envelope      : {(envelope.HasValue ? $"[{envelope.Value.Item1}, {envelope.Value.Item2}]" : "(none)")}");

            if (total < 3)
            {
                Console.WriteLine($"FAIL: expected ≥ 3 line samples, saw {total}");
                return 1;
            }
            if (!envelope.HasValue)
            {
                Console.WriteLine("FAIL: envelope was not populated.");
                return 1;
            }
            if (envelope.Value.Item1 > envelope.Value.Item2)
            {
                Console.WriteLine($"FAIL: envelope inverted ({envelope.Value.Item1} > {envelope.Value.Item2}).");
                return 1;
            }
            if (double.IsNaN(envelope.Value.Item1) || double.IsNaN(envelope.Value.Item2)
                || double.IsInfinity(envelope.Value.Item1) || double.IsInfinity(envelope.Value.Item2))
            {
                Console.WriteLine("FAIL: envelope has non-finite bounds.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("LINES PROBE PASS");
            return 0;
        }
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.LinesProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.LinesProbe");
    }
}
