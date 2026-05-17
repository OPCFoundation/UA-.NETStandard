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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens;

/// <summary>
/// Headless smoke test: drives the adapters against a running OPC UA server
/// without opening the TUI. Used to validate end-to-end wiring in CI and from
/// CLI when the interactive UI cannot be opened.
/// </summary>
internal static class SmokeTest
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine($"== UaLens smoke test ==");
        Console.WriteLine($"   endpoint: {endpointUrl}");
        Console.WriteLine();

        int rc = 0;
        rc |= await RunOneAsync(SubscriptionEngineKind.ChannelV2, endpointUrl, telemetry, ct).ConfigureAwait(false);
        rc |= await RunOneAsync(SubscriptionEngineKind.Classic, endpointUrl, telemetry, ct).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine(rc == 0 ? "SMOKE PASS" : "SMOKE FAIL");
        return rc;
    }

    private static async Task<int> RunOneAsync(
        SubscriptionEngineKind engine, string endpointUrl, ITelemetryContext telemetry, CancellationToken ct)
    {
        Console.WriteLine($"--- engine: {engine} ---");
        var conn = new ConnectionService(telemetry);
        try
        {
            await conn.ConnectAsync(new ConnectionOptions { EndpointUrl = endpointUrl, Engine = engine }, ct)
                .ConfigureAwait(false);

            ISubscriptionAdapter a = conn.CreateAdapter();
            if (a is null)
            {
                Console.WriteLine("  no adapter after connect");
                return 1;
            }

            await a.ApplySubscriptionAsync(new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(1000),
                KeepAliveCount = 10,
                LifetimeCount = 1000,
                MaxNotificationsPerPublish = 1000,
                PublishingEnabled = true,
                // Engine knobs — exercise the new settings path.
                MinPublishRequestCount = 4,
                MaxPublishRequestCount = 12
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

            // Drain channel concurrently so it doesn't fill (animation view does this in TUI mode).
            using var draindone = new CancellationTokenSource();
            Task drain = Task.Run(async () =>
            {
                while (!draindone.IsCancellationRequested)
                {
                    if (await a.Events.WaitToReadAsync(draindone.Token).ConfigureAwait(false))
                    {
                        while (a.Events.TryRead(out _))
                        {
                            // discard
                        }
                    }
                }
            }, draindone.Token);

            // Run for 5 seconds.
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            int rc = 0;
            int data = (int)a.Counters.DataMessages;
            int kas = (int)a.Counters.KeepAlives;
            int values = (int)(a.Counters.DataValues + a.Counters.EventValues);
            double revPubMs = a.CurrentPublishingInterval.TotalMilliseconds;
            uint revKa = a.CurrentKeepAliveCount;

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  itemId={0}  revPub={1:0}ms  revKA={2}  data={3}  ka={4}  values={5}",
                id, revPubMs, revKa, data, kas, values));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  workers={0}  in-flight={1}  bad={2}  missing={3}  republish={4}",
                a.PublishWorkerCount, a.GoodPublishRequestCount, a.BadPublishRequestCount,
                a.MissingMessageCount, a.RepublishMessageCount));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  minWorkers={0} maxWorkers={1}  minReq={2} maxReq={3}",
                a.MinPublishWorkerCount, a.MaxPublishWorkerCount,
                a.MinPublishRequestCount, a.MaxPublishRequestCount));

            // Validation: against ConsoleReferenceServer with default 1000 ms simulation,
            // we expect ~5 data messages over 5s. Allow a wide band for slow CI machines.
            if (data < 2 || data > 12)
            {
                Console.WriteLine($"  FAIL: expected 2..12 data messages, saw {data}");
                rc = 1;
            }
            if (revPubMs <= 0)
            {
                Console.WriteLine($"  FAIL: revised publishing interval not reported");
                rc = 1;
            }

            await a.RemoveItemAsync(id, ct).ConfigureAwait(false);
            draindone.Cancel();
            try
            { await drain.ConfigureAwait(false); }
            catch { /* expected */ }
            return rc;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            await conn.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.Smoke");
        public ActivitySource ActivitySource { get; } = new("UaLens.Smoke");
    }
}
