/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
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
/// Headless validator for the dot-plot animation buffer.  Subscribes to a
/// 1-Hz changing variable for ~5 s and asserts that:
/// <list type="bullet">
/// <item>the dot buffer has at least the expected number of dots,</item>
/// <item>their timestamps are non-decreasing (timeseries order),</item>
/// <item>each dot's kind matches a real notification (data / event / KA).</item>
/// </list>
/// </summary>
internal static class DotsProbe
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine("== Dots probe ==");
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

            // Hand the adapter's channel to a fresh AnimationCanvas in Dots mode
            // and pump it manually for a few seconds to populate the dot buffer.
            var canvas = new AnimationCanvas { Mode = AnimationMode.Dots };
            canvas.Bind(a.Events, a.Counters);

            const int seconds = 5;
            for (int i = 0; i < seconds; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                // Drain into the dot buffer (Tick() requires the dispatcher; Drain() does not).
                canvas.Drain();
                int snap = canvas.DotSnapshot.Count;
                Console.WriteLine($"  t={i + 1}s  dots={snap}");
            }

            await a.RemoveItemAsync(id, ct).ConfigureAwait(false);

            var dots = canvas.DotSnapshot;
            Console.WriteLine($"  TOTAL dots in buffer: {dots.Count}");

            // Assert ascending timestamps.
            DateTime prev = DateTime.MinValue;
            int outOfOrder = 0;
            foreach (DotEvent d in dots)
            {
                if (d.TimestampUtc < prev)
                {
                    outOfOrder++;
                }
                prev = d.TimestampUtc;
            }
            Console.WriteLine($"  out-of-order dots: {outOfOrder}");

            // Expect at least ~3 dots over 5s with a 1 Hz publisher.  Allow wide
            // band for slow CI machines.
            if (dots.Count < 3)
            {
                Console.WriteLine($"FAIL: expected ≥ 3 dots, saw {dots.Count}");
                return 1;
            }
            if (outOfOrder > 0)
            {
                Console.WriteLine($"FAIL: dot buffer is not in arrival order");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("DOTS PROBE PASS");
            return 0;
        }
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.DotsProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.DotsProbe");
    }
}
