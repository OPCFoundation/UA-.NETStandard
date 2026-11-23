// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.Perf
{
    /// <summary>
    /// Measures the server fixture lifecycle the live test suites pay per
    /// fixture: server configuration, server start, client configuration,
    /// connect, session close and server stop.
    /// </summary>
    /// <remarks>
    /// Explicit: a measurement, not a check. The PKI lives in a fixed folder
    /// under the temp path so certificates are created once per machine and
    /// every later run measures warm stores. Tune with environment variables:
    /// <c>OPCUA_STARTUP_PROBE_CYCLES</c> (default 10),
    /// <c>OPCUA_STARTUP_PROBE_WORKERS</c> (parallel cycles, default 1),
    /// <c>OPCUA_STARTUP_PROBE_PKI</c> (PKI folder),
    /// <c>OPCUA_STARTUP_PROBE_FRESH_PKI</c> (<c>1</c>: a new PKI folder per cycle, so
    /// every cycle also creates its certificates) and
    /// <c>OPCUA_STARTUP_PROBE_OUT</c> (file the per-cycle CSV is appended to).
    /// See plans/server-startup-profile.md.
    /// </remarks>
    [TestFixture]
    [Explicit("Startup/shutdown measurement; run on demand.")]
    [Category("Perf")]
    [NonParallelizable]
    public sealed class ServerStartupProbeTests
    {
        private static readonly string[] s_phases =
        [
            "ServerLoad",
            "ServerStart",
            "ClientLoad",
            "Connect",
            "Close",
            "ServerStop"
        ];

        [Test]
        public Task StandardServerLifecycleAsync()
        {
            return RunAsync(
                "StandardServer",
                () => new ServerFixture<StandardServer>(t => new StandardServer(t)) { AutoAccept = true });
        }

        [Test]
        public Task ReferenceServerLifecycleAsync()
        {
            return RunAsync(
                "ReferenceServer",
                () => new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
                {
                    AutoAccept = true,
                    OperationLimits = true
                });
        }

        private static async Task RunAsync<T>(string name, Func<ServerFixture<T>> createFixture)
            where T : ServerBase
        {
            int cycles = ReadInt("OPCUA_STARTUP_PROBE_CYCLES", 10);
            int workers = Math.Max(1, ReadInt("OPCUA_STARTUP_PROBE_WORKERS", 1));
            string pkiRoot = Environment.GetEnvironmentVariable("OPCUA_STARTUP_PROBE_PKI") ??
                Path.Combine(Path.GetTempPath(), "OPC", "startup-probe-pki");
            string? outFile = Environment.GetEnvironmentVariable("OPCUA_STARTUP_PROBE_OUT");
            bool freshPki = Environment.GetEnvironmentVariable("OPCUA_STARTUP_PROBE_FRESH_PKI") == "1";
            string kind = freshPki ? "fresh-pki" : "warm";

            // First cycle of the process is cold (JIT, type initialisation).
            double[] cold = await RunCycleAsync(createFixture, pkiRoot, freshPki).ConfigureAwait(false);
            Report(name, "cold", 1, [cold], outFile);

            var samples = new List<double[]>();
            for (int done = 0; done < cycles; done += workers)
            {
                int batch = Math.Min(workers, cycles - done);
                double[][] results = await Task.WhenAll(
                        Enumerable.Range(0, batch)
                            .Select(_ => Task.Run(() => RunCycleAsync(createFixture, pkiRoot, freshPki))))
                    .ConfigureAwait(false);
                samples.AddRange(results);
            }
            Report(name, kind, workers, samples, outFile);
        }

        private static async Task<double[]> RunCycleAsync<T>(
            Func<ServerFixture<T>> createFixture,
            string pkiRoot,
            bool freshPki)
            where T : ServerBase
        {
            if (freshPki)
            {
                pkiRoot = Path.Combine(Path.GetTempPath(), "OPC", "startup-probe-fresh", Path.GetRandomFileName());
            }
            try
            {
                return await RunCycleCoreAsync(createFixture, pkiRoot).ConfigureAwait(false);
            }
            finally
            {
                if (freshPki)
                {
                    try
                    {
                        Directory.Delete(pkiRoot, true);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private static async Task<double[]> RunCycleCoreAsync<T>(
            Func<ServerFixture<T>> createFixture,
            string pkiRoot)
            where T : ServerBase
        {
            double[] times = new double[s_phases.Length + 1];
            ServerFixture<T> server = createFixture();
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();
            await server.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
            times[0] = Lap(sw);
            await server.StartAsync().ConfigureAwait(false);
            times[1] = Lap(sw);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new ClientFixture(telemetry);
            try
            {
                await client.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
                times[2] = Lap(sw);
                var url = new Uri(
                    Utils.UriSchemeOpcTcp + "://localhost:" +
                    server.Port.ToString(CultureInfo.InvariantCulture));
                ISession session = await client
                    .ConnectAsync(url, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                times[3] = Lap(sw);
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            times[4] = Lap(sw);
            await server.StopAsync().ConfigureAwait(false);
            times[5] = Lap(sw);
            times[6] = total.Elapsed.TotalMilliseconds;
            return times;
        }

        private static double Lap(Stopwatch sw)
        {
            double ms = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            return ms;
        }

        private static void Report(
            string name,
            string kind,
            int workers,
            List<double[]> samples,
            string? outFile)
        {
            var text = new StringBuilder();
            text.Append(FormattableString.Invariant($"{name} {kind} n={samples.Count} workers={workers} (median ms):"));
            for (int i = 0; i <= s_phases.Length; i++)
            {
                string phase = i < s_phases.Length ? s_phases[i] : "Total";
                double median = Median(samples.Select(s => s[i]));
                text.Append(FormattableString.Invariant($" {phase}={median:F1}"));
            }
            TestContext.Out.WriteLine(text.ToString());
            TestContext.Progress.WriteLine(text.ToString());

            if (!string.IsNullOrEmpty(outFile))
            {
                var csv = new StringBuilder();
                foreach (double[] s in samples)
                {
                    csv.Append(name).Append(',').Append(kind).Append(',')
                        .Append(workers.ToString(CultureInfo.InvariantCulture));
                    foreach (double v in s)
                    {
                        csv.Append(',').Append(v.ToString("F2", CultureInfo.InvariantCulture));
                    }
                    csv.AppendLine();
                }
                File.AppendAllText(outFile, csv.ToString());
            }
        }

        private static double Median(IEnumerable<double> values)
        {
            double[] sorted = [.. values];
            Array.Sort(sorted);
            if (sorted.Length == 0)
            {
                return 0;
            }
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        private static int ReadInt(string variable, int fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : fallback;
        }
    }
}
