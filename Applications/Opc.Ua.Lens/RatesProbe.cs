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

namespace UaLens;

/// <summary>
/// Reproduces the user-reported issue: at very fast publishing intervals
/// (10 ms) only keep-alive notifications surface, while data notifications
/// disappear; at moderate intervals (100 ms) only data notifications
/// surface even though keep-alives should also fire (KA timeout =
/// KeepAliveCount × publishingInterval).
///
/// Adds a 1-Hz changing variable (Scalar_Simulation_UInt32, sampling=1000ms)
/// then runs three windows back-to-back with publishing intervals 10 ms,
/// 100 ms, 1000 ms and reports the per-second mix of data / KA notifications
/// reaching the channel adapter.
/// </summary>
internal static class RatesProbe
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine("== Rates probe ==");
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

            // Add a 1-Hz changing item.
            await a.ApplySubscriptionAsync(new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(1000),
                KeepAliveCount = 10,
                LifetimeCount = 100
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

            int rc = 0;
            rc |= await OneCaseAsync(a, pubMs: 1000, ka: 10, seconds: 5, ct).ConfigureAwait(false);
            rc |= await OneCaseAsync(a, pubMs: 100, ka: 10, seconds: 5, ct).ConfigureAwait(false);
            rc |= await OneCaseAsync(a, pubMs: 10, ka: 10, seconds: 5, ct).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine(rc == 0 ? "RATES PROBE PASS" : "RATES PROBE FAIL");
            return rc;
        }
    }

    private static async Task<int> OneCaseAsync(ISubscriptionAdapter a, int pubMs, uint ka, int seconds, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"--- pub={pubMs}ms KA={ka}  (KA timeout = {pubMs * ka} ms) ---");

        // Re-apply with new pub/KA.
        await a.ApplySubscriptionAsync(new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(pubMs),
            KeepAliveCount = ka,
            LifetimeCount = Math.Max(100, ka * 10)
        }, ct).ConfigureAwait(false);

        // Drain & count.
        long startData = a.Counters.DataMessages;
        long startKa = a.Counters.KeepAlives;
        // Also drain channel so it doesn't fill.
        using var stop = new CancellationTokenSource();
        Task drain = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (await a.Events.WaitToReadAsync(stop.Token).ConfigureAwait(false))
                    {
                        while (a.Events.TryRead(out _))
                        { }
                    }
                }
                catch (OperationCanceledException) { return; }
            }
        }, stop.Token);

        for (int s = 0; s < seconds; s++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            long data = a.Counters.DataMessages - startData;
            long kaCount = a.Counters.KeepAlives - startKa;
            Console.WriteLine(
                $"  t={s + 1}s  data={data}  ka={kaCount}  " +
                $"workers={a.PublishWorkerCount}  in-flight={a.GoodPublishRequestCount}  " +
                $"bad={a.BadPublishRequestCount}  missing={a.MissingMessageCount}  " +
                $"republish={a.RepublishMessageCount}");
        }

        stop.Cancel();
        try
        { await drain.ConfigureAwait(false); }
        catch { }

        long totalData = a.Counters.DataMessages - startData;
        long totalKa = a.Counters.KeepAlives - startKa;
        Console.WriteLine($"  TOTAL   data={totalData}  ka={totalKa}");

        // Sanity: with a 1-Hz changing item, we expect ~5 data over 5 s regardless
        // of publishing interval; for pub=10ms and pub=100ms (KA timeout < 1s) we
        // also expect KA notifications during the silent gaps.
        bool expectKa = pubMs * ka < 1000;          // KA timeout < gap between data
        if (totalData < 1)
        {
            Console.WriteLine("  FAIL: zero data notifications received");
            return 1;
        }
        if (expectKa && totalKa == 0)
        {
            Console.WriteLine($"  FAIL: expected KA notifications (timeout {pubMs * ka} ms < data gap 1000 ms) but got zero");
            return 1;
        }
        return 0;
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.RatesProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.RatesProbe");
    }
}
