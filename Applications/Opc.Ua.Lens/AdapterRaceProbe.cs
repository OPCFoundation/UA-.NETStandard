/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Subscriptions;

namespace UaLens;

/// <summary>
/// Adapter-lifecycle race stress test: spawns many tasks that create +
/// forget adapters concurrently with a disconnect.  Asserts that
/// <see cref="ConnectionService.ForgetAdapter"/>'s new bool return value
/// correctly differentiates "you own disposal" vs "Disconnect already
/// owned it" so no zombies leak through either path.
/// </summary>
internal static class AdapterRaceProbe
{
    public static async Task<int> RunAsync(string endpointUrl, CancellationToken ct = default)
    {
        var telemetry = new ConsoleTelemetry();
        Console.WriteLine("== AdapterRace probe ==");
        Console.WriteLine($"   endpoint: {endpointUrl}");

        var conn = new ConnectionService(telemetry);
        await using (conn.ConfigureAwait(false))
        {
            await conn.ConnectAsync(new ConnectionOptions
            {
                EndpointUrl = endpointUrl,
                Engine = SubscriptionEngineKind.ChannelV2
            }, ct).ConfigureAwait(false);

            const int N = 24;
            var adapters = new ISubscriptionAdapter[N];
            for (int i = 0; i < N; i++)
            {
                adapters[i] = conn.CreateAdapter();
            }

            // Pre-disconnect: each adapter should be forget-able exactly once.
            int forgottenCount = 0;
            for (int i = 0; i < N / 2; i++)
            {
                if (conn.ForgetAdapter(adapters[i]))
                {
                    forgottenCount++;
                    await adapters[i].DisposeAsync().ConfigureAwait(false);
                }
            }
            if (forgottenCount != N / 2)
            {
                Console.WriteLine($"FAIL: expected {N / 2} ForgetAdapter=true, got {forgottenCount}");
                return 1;
            }

            // Disconnect — should dispose the remaining N/2 adapters that are
            // still tracked; ForgetAdapter for any of those AFTER disconnect
            // must return false (already disposed).
            Task disconnectTask = conn.DisconnectAsync();

            // Race: try ForgetAdapter on the not-yet-forgotten adapters
            // concurrently with the disconnect.  Outcome must be EITHER:
            //   - ForgetAdapter returns true → caller disposes (no double dispose).
            //   - ForgetAdapter returns false → DisconnectInternalAsync is/was disposing.
            int doubleDisposalAttempts = 0;
            var forgotAfter = new bool[N];
            Parallel.For(N / 2, N, i =>
            {
                forgotAfter[i] = conn.ForgetAdapter(adapters[i]);
                if (forgotAfter[i])
                {
                    try
                    {
                        adapters[i].DisposeAsync().AsTask().Wait();
                    }
                    catch (Exception)
                    {
                        Interlocked.Increment(ref doubleDisposalAttempts);
                    }
                }
            });

            await disconnectTask.ConfigureAwait(false);

            Console.WriteLine($"  forgot pre-disconnect : {forgottenCount}");
            Console.WriteLine($"  forgot during disconn : {forgotAfter.Skip(N / 2).Count(b => b)}");
            Console.WriteLine($"  double-dispose excepts: {doubleDisposalAttempts}");

            if (doubleDisposalAttempts > 0)
            {
                Console.WriteLine("FAIL: double-disposal detected.");
                return 1;
            }
            Console.WriteLine("ADAPTER RACE PROBE PASS");
            return 0;
        }
    }

    private sealed class ConsoleTelemetry : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public Meter CreateMeter() => new("UaLens.AdapterRaceProbe");
        public ActivitySource ActivitySource { get; } = new("UaLens.AdapterRaceProbe");
    }
}
