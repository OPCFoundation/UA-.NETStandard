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
/// Probe specifically designed to surface the V2 keep-alive bug claim.
/// Creates a subscription with NO monitored items and waits long enough that
/// the server must transmit at least one server-side keep-alive (per OPC UA
/// Part 4: server emits an empty NotificationMessage every
/// <c>KeepAliveCount × PublishingInterval</c> when no data is queued).
/// </summary>
internal static class KeepAliveProbe
{
    public static async Task<int> RunAsync(string endpointUrl, int waitSeconds, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine($"== KeepAlive probe ==");
        Console.WriteLine($"   endpoint: {endpointUrl}   wait: {waitSeconds}s   no monitored items");

        int rc = 0;
        rc |= await ProbeOneAsync(SubscriptionEngineKind.ChannelV2, endpointUrl, waitSeconds, telemetry, ct).ConfigureAwait(false);
        rc |= await ProbeOneAsync(SubscriptionEngineKind.Classic, endpointUrl, waitSeconds, telemetry, ct).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine(rc == 0 ? "KA PROBE PASS" : "KA PROBE FAIL");
        return rc;
    }

    private static async Task<int> ProbeOneAsync(
        SubscriptionEngineKind engine, string endpointUrl, int waitSeconds,
        ITelemetryContext telemetry, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"--- engine: {engine} ---");
        var conn = new ConnectionService(telemetry);
        await using (conn.ConfigureAwait(false))
        {
            try
            {
                await conn.ConnectAsync(new ConnectionOptions { EndpointUrl = endpointUrl, Engine = engine }, ct).ConfigureAwait(false);
                ISubscriptionAdapter a = conn.CreateAdapter();
                if (a is null)
                {
                    Console.WriteLine("  no adapter");
                    return 1;
                }
                // Tight KA so we don't have to wait minutes:
                //   KA=3 publishes × pub=1000 ms → KA timeout ~3 s
                await a.ApplySubscriptionAsync(new SubscriptionConfig
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(1000),
                    KeepAliveCount = 3,
                    LifetimeCount = 100,
                    MaxNotificationsPerPublish = 1000,
                    PublishingEnabled = true
                }, ct).ConfigureAwait(false);

                // Drain notifications channel to keep counters up to date.
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

                for (int s = 0; s < waitSeconds; s++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    Console.WriteLine(
                        $"  t={s + 1,2}s  data={a.Counters.DataMessages}  ka={a.Counters.KeepAlives}  " +
                        $"workers={a.PublishWorkerCount}  in-flight={a.GoodPublishRequestCount}");
                }

                stop.Cancel();
                try
                { await drain.ConfigureAwait(false); }
                catch { }

                long ka = a.Counters.KeepAlives;
                Console.WriteLine($"  TOTAL keep-alives: {ka}");
                if (ka == 0)
                {
                    Console.WriteLine($"  WARN: zero keep-alives received in {waitSeconds}s with KA timeout ~3s.");
                    return engine == SubscriptionEngineKind.ChannelV2 ? 1 : 0;   // V2 expected to deliver
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.KaProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.KaProbe");
    }
}
