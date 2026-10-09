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


// Server load / profiling harness. Not part of the product; see perf/README.md.
// Modes:
//   server  --port P [--diag 0|1] [--audit 0|1] [--threads N]
//           Starts a ReferenceServer and reads commands from stdin: mark | report | quit
//   run     --scenario read|write|sub|browse [--sessions N] [--nodes K] [--inflight I]
//           [--duration S] [--warmup S] [--security none|sign|encrypt] [--diag 0|1] [--audit 0|1]
//           [--subs S] [--pub ms] [--write-interval ms] [--trace file] [--trace-profile p] [--out file]
//           Spawns the server child, drives the load and prints a report of client latency
//           and server-side CPU / GC / allocation deltas for the measurement window.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Entry point of the harness.
    /// </summary>
    public static class Program
    {
        private static readonly Dictionary<string, string> s_args = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Runs the server or the load driver.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    string key = args[i][2..];
                    string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        ? args[++i]
                        : "1";
                    s_args[key] = value;
                }
            }
            string mode = args.Length > 0 ? args[0] : "run";
            return mode == "server"
                ? await RunServerAsync().ConfigureAwait(false)
                : await RunDriverAsync(args).ConfigureAwait(false);
        }

        internal static string Arg(string name, string defaultValue)
        {
            return s_args.TryGetValue(name, out string? value) ? value : defaultValue;
        }

        internal static int ArgInt(string name, int defaultValue)
        {
            return int.Parse(
                Arg(name, defaultValue.ToString(CultureInfo.InvariantCulture)),
                CultureInfo.InvariantCulture);
        }

        internal static List<NodeId> GetNodes(ISession session, int count, Type? onlyType = null)
        {
            var result = new List<NodeId>();
            foreach ((Type type, ExpandedNodeId[] ids) in CommonTestWorkers.NodeIdTestSetStaticMassNumeric)
            {
                if (onlyType != null && type != onlyType)
                {
                    continue;
                }
                foreach (ExpandedNodeId id in ids)
                {
                    if (result.Count == count)
                    {
                        return result;
                    }
                    result.Add(ExpandedNodeId.ToNodeId(id, session.NamespaceUris));
                }
            }
            return result;
        }

        private static ITelemetryContext CreateTelemetry(LogLevel level)
        {
            return DefaultTelemetry.Create(b => b.SetMinimumLevel(level).AddSimpleConsole(o => o.SingleLine = true));
        }

        private static async Task<int> RunServerAsync()
        {
            ITelemetryContext telemetry = CreateTelemetry(LogLevel.Error);
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = "PerfReferenceServer",
                ApplicationType = ApplicationType.Server
            };
            try
            {
                return await RunServerAsync(application, telemetry).ConfigureAwait(false);
            }
            finally
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task<int> RunServerAsync(ApplicationInstance application, ITelemetryContext telemetry)
        {
            int port = ArgInt("port", 62555);
            bool diagnostics = ArgInt("diag", 0) != 0;
            bool auditing = ArgInt("audit", 0) != 0;
            string pkiRoot = Path.Combine(Path.GetTempPath(), "uaperf-pki-server");
            string endpointUrl = FormattableString.Invariant($"opc.tcp://localhost:{port}/PerfReferenceServer");
            ArrayOf<CertificateIdentifier> certificates =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    "CN=PerfReferenceServer, O=OPC Foundation, DC=localhost",
                    CertificateStoreType.Directory,
                    pkiRoot);
            ApplicationConfiguration config = await application
                .Build("urn:localhost:UA:PerfReferenceServer", "uri:opcfoundation.org:PerfReferenceServer")
                .SetMaxByteStringLength(4 * 1024 * 1024)
                .SetMaxArrayLength(1024 * 1024)
                .SetMaxMessageSize(16 * 1024 * 1024)
                .AsServer([endpointUrl])
                .AddUnsecurePolicyNone()
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .SetMaxChannelCount(2100)
                .SetDiagnosticsEnabled(diagnostics)
                .SetAuditingEnabled(auditing)
                .SetShutdownDelay(0)
                .AddSecurityConfiguration(certificates, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync()
                .ConfigureAwait(false);
            config.ServerConfiguration!.MaxSessionCount = 2000;
            config.ServerConfiguration.MaxSubscriptionCount = 20000;
            config.ServerConfiguration.MaxFailedAuthenticationAttempts = 0;
            config.ServerConfiguration.MaxRequestThreadCount = ArgInt("threads", 200);
            config.ServerConfiguration.MinRequestThreadCount = 50;
            // sized for the largest pipeline (sessions x inflight), as the o6 benchmarks do
            config.ServerConfiguration.MaxQueuedRequestCount = ArgInt("queue", 2000);
            int counters = ArgInt("counters", 0);
            int sampling = ArgInt("sampling", 10);
            if (counters > 0)
            {
                // the subscription capacity workload of o6-automation/opcua-benchmarks
                config.ServerConfiguration.MinPublishingInterval = 1;
                config.ServerConfiguration.PublishingResolution = 1;
                config.ServerConfiguration.MaxPublishingInterval = 86400_000;
                config.ServerConfiguration.MaxNotificationQueueSize = 1;
                config.ServerConfiguration.MaxNotificationsPerPublish = int.MaxValue;
                config.ServerConfiguration.MaxPublishRequestCount = 4;
                config.ServerConfiguration.AvailableSamplingRates = [new SamplingRateGroup(sampling, 1, 0)];
            }

            if (!await application
                .CheckApplicationInstanceCertificatesAsync(true, CertificateFactory.DefaultLifeTime)
                .ConfigureAwait(false))
            {
                Console.WriteLine("ERROR cert");
                return 1;
            }

            using Opc.Ua.Server.StandardServer server = counters > 0
                ? new CounterServer(telemetry, counters)
                : new ReferenceServer(telemetry);
            server.TransportBindings = TestTransportBindings.WithAllSchemes();
            await application.StartAsync(server).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant($"READY {endpointUrl} {Environment.ProcessId}"));

            ServerStats mark = ServerStats.Capture();
            string? line;
            while ((line = await Console.In.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                switch (line.Trim())
                {
                    case "mark":
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        mark = ServerStats.Capture();
                        (server as CounterServer)?.Counters?.Mark();
                        Console.WriteLine("MARKED");
                        break;
                    case "report":
                        string counterReport = (server as CounterServer)?.Counters?.Report(sampling) ?? string.Empty;
                        Console.WriteLine("REPORT " + ServerStats.Capture().Delta(mark) + " " + counterReport);
                        break;
                    case "quit":
                        await server.StopAsync().ConfigureAwait(false);
                        return 0;
                }
            }
            return 0;
        }

        private static async Task<int> RunDriverAsync(string[] args)
        {
            string scenario = Arg("scenario", "read");
            int sessionCount = ArgInt("sessions", 20);
            int durationSeconds = ArgInt("duration", 20);
            int warmupSeconds = ArgInt("warmup", 10);
            int port = ArgInt("port", 62555 + (Environment.ProcessId % 1000));
            string security = Arg("security", "none");

            var startInfo = new ProcessStartInfo(
                Environment.ProcessPath!,
                FormattableString.Invariant(
                    $"server --port {port} --diag {Arg("diag", "0")} --audit {Arg("audit", "0")} --threads {Arg("threads", "200")} --counters {(scenario == "counters" ? Arg("nodes", "1") : "0")} --sampling {Arg("sampling", "10")} --queue {Arg("queue", "2000")}"))
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                string name = (string)variable.Key;
                if (name.StartsWith("SERVER_", StringComparison.Ordinal))
                {
                    startInfo.Environment[name[7..]] = (string?)variable.Value;
                }
            }

            using Process server = Process.Start(startInfo)!;
            using var serverLines = new BlockingCollection<string>();
            _ = Task.Run(() => PumpServerOutput(server, serverLines));
            string ready = serverLines.Take();
            if (!ready.StartsWith("READY", StringComparison.Ordinal))
            {
                Console.WriteLine(ready);
                return 1;
            }
            string url = ready.Split(' ')[1];
            Console.WriteLine(FormattableString.Invariant($"server pid={server.Id} url={url}"));

            ITelemetryContext telemetry = CreateTelemetry(LogLevel.Error);
            using var client = new ClientFixture(telemetry) { SessionTimeout = 60000, OperationTimeout = 60000 };
            await client
                .LoadClientConfigurationAsync(Path.Combine(Path.GetTempPath(), "uaperf-pki-client"), "PerfClient")
                .ConfigureAwait(false);
            string policy = security == "none" ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256;
            MessageSecurityMode mode = security switch
            {
                "sign" => MessageSecurityMode.Sign,
                "encrypt" => MessageSecurityMode.SignAndEncrypt,
                _ => MessageSecurityMode.None
            };
            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(new Uri(url)).ConfigureAwait(false);
            var selected = new List<EndpointDescription>();
            foreach (EndpointDescription endpoint in endpoints)
            {
                if (endpoint.SecurityPolicyUri == policy && endpoint.SecurityMode == mode)
                {
                    selected.Add(endpoint);
                }
            }

            var sessions = new ISession[sessionCount];
            var connectTime = Stopwatch.StartNew();
            await Parallel.ForAsync(
                0,
                sessionCount,
                new ParallelOptions { MaxDegreeOfParallelism = 32 },
                async (i, ct) => sessions[i] = await client
                    .ConnectAsync(new Uri(url), policy, selected)
                    .ConfigureAwait(false))
                .ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"connected {sessionCount} sessions ({security}) in {connectTime.ElapsedMilliseconds} ms"));

            Workload workload = scenario == "counters"
                ? new CounterWorkload(sessions[0], ArgInt("nodes", 1), ArgInt("sampling", 10), ArgInt("pub", 1000))
                : scenario == "sub"
                ? new SubscribeWorkload(
                    sessions,
                    ArgInt("nodes", 100),
                    ArgInt("subs", 5),
                    ArgInt("pub", 100),
                    ArgInt("write-interval", 100))
                : new RequestWorkload(scenario, sessions, ArgInt("nodes", 100), ArgInt("inflight", 1));
            await workload.SetupAsync().ConfigureAwait(false);

            using var stop = new CancellationTokenSource();
            Task run = workload.RunAsync(stop.Token);
            await Task.Delay(TimeSpan.FromSeconds(warmupSeconds)).ConfigureAwait(false);

            using Process? tracer = StartTracer(server.Id, durationSeconds, out string? trace);
            if (tracer != null)
            {
                await Task.Delay(2000).ConfigureAwait(false);
            }

            await server.StandardInput.WriteLineAsync("mark").ConfigureAwait(false);
            Take(serverLines, "MARKED");
            workload.ResetStats();
            var window = Stopwatch.StartNew();
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ConfigureAwait(false);
            await server.StandardInput.WriteLineAsync("report").ConfigureAwait(false);
            string report = Take(serverLines, "REPORT")[7..];
            string clientReport = workload.Report(window.Elapsed.TotalSeconds);
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // the workload stops by cancellation
            }

            if (tracer != null)
            {
                await tracer.WaitForExitAsync().ConfigureAwait(false);
                Console.WriteLine("trace: " + trace);
            }

            var result = new StringBuilder();
            result.Append(CultureInfo.InvariantCulture, $"RESULT scenario={scenario} security={security} sessions={sessionCount} ")
                .Append(string.Join(' ', args.Skip(1)))
                .Append('\n')
                .Append("  client: ").Append(clientReport).Append('\n')
                .Append("  server: ").Append(report).Append('\n');
            double operations = workload.Operations;
            if (operations > 0)
            {
                (double allocatedMb, double cpuMs) = ParseServerReport(report);
                result.Append(
                    CultureInfo.InvariantCulture,
                    $"  per-op: allocB={allocatedMb * 1048576 / operations:F0} cpuUs={cpuMs * 1000 / operations:F1}  ({workload.OperationName})\n");
            }
            Console.Write(result.ToString());
            if (s_args.TryGetValue("out", out string? outFile))
            {
                await File.AppendAllTextAsync(outFile, result.ToString()).ConfigureAwait(false);
            }

            await Parallel.ForEachAsync(sessions, async (session, ct) =>
            {
                try
                {
                    await session.CloseAsync(ct).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // the server may already be shutting the session down
                }
                session.Dispose();
            }).ConfigureAwait(false);
            await server.StandardInput.WriteLineAsync("quit").ConfigureAwait(false);
            if (!server.WaitForExit(10000))
            {
                server.Kill();
            }
            return 0;
        }

        private static void PumpServerOutput(Process server, BlockingCollection<string> lines)
        {
            string? line;
            while ((line = server.StandardOutput.ReadLine()) != null)
            {
                if (line.StartsWith("READY", StringComparison.Ordinal) ||
                    line.StartsWith("REPORT", StringComparison.Ordinal) ||
                    line.StartsWith("MARKED", StringComparison.Ordinal) ||
                    line.StartsWith("ERROR", StringComparison.Ordinal))
                {
                    lines.Add(line);
                }
                else if (s_args.ContainsKey("verbose"))
                {
                    Console.WriteLine("[server] " + line);
                }
            }
            lines.CompleteAdding();
        }

        private static Process? StartTracer(int processId, int durationSeconds, out string? trace)
        {
            if (!s_args.TryGetValue("trace", out trace))
            {
                return null;
            }
            string providers = Arg("trace-profile", "cpu") switch
            {
                // allocation sampling (GCAllocationTick, about every 100 KB) and GC events
                "alloc" => "--providers Microsoft-Windows-DotNETRuntime:0x1:5",
                "gc" => "--profile gc-verbose",
                // the Contention keyword
                "contention" => "--providers Microsoft-Windows-DotNETRuntime:0x4000:4",
                _ => "--profile dotnet-sampled-thread-time"
            };
            return Process.Start(new ProcessStartInfo(
                "dotnet-trace",
                FormattableString.Invariant(
                    $"collect --process-id {processId} {providers} -o \"{trace}\" --duration 00:00:{durationSeconds:D2}"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }

        private static string Take(BlockingCollection<string> lines, string prefix)
        {
            while (true)
            {
                string line = lines.Take();
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line;
                }
            }
        }

        private static (double AllocatedMb, double CpuMs) ParseServerReport(string report)
        {
            Dictionary<string, string> values = report
                .Split(' ')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
            return (
                double.Parse(values["allocMB"], CultureInfo.InvariantCulture),
                double.Parse(values["cpuMs"], CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Process-wide counters of the server process, captured at the start and end of the window.
    /// </summary>
    internal sealed record ServerStats(
        long Allocated,
        int Gen0,
        int Gen1,
        int Gen2,
        TimeSpan Pause,
        TimeSpan Cpu,
        long Contention,
        long Timestamp,
        int Threads,
        long WorkingSet,
        long Heap)
    {
        public static ServerStats Capture()
        {
            using var process = Process.GetCurrentProcess();
            return new ServerStats(
                GC.GetTotalAllocatedBytes(true),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                GC.GetTotalPauseDuration(),
                process.TotalProcessorTime,
                Monitor.LockContentionCount,
                Stopwatch.GetTimestamp(),
                ThreadPool.ThreadCount,
                process.WorkingSet64,
                GC.GetGCMemoryInfo().HeapSizeBytes);
        }

        public string Delta(ServerStats mark)
        {
            double seconds = Stopwatch.GetElapsedTime(mark.Timestamp, Timestamp).TotalSeconds;
            TimeSpan cpu = Cpu - mark.Cpu;
            return FormattableString.Invariant(
                $"sec={seconds:F2} allocMB={(Allocated - mark.Allocated) / 1048576.0:F1} gen0={Gen0 - mark.Gen0} ") +
                FormattableString.Invariant(
                $"gen1={Gen1 - mark.Gen1} gen2={Gen2 - mark.Gen2} pauseMs={(Pause - mark.Pause).TotalMilliseconds:F0} ") +
                FormattableString.Invariant(
                $"cpuMs={cpu.TotalMilliseconds:F0} cpuCores={cpu.TotalSeconds / seconds:F2} ") +
                FormattableString.Invariant(
                $"contention={Contention - mark.Contention} threads={Threads} wsMB={WorkingSet / 1048576} heapMB={Heap / 1048576}");
        }
    }

    /// <summary>
    /// Latency samples (Stopwatch ticks) recorded by many threads. ConcurrentBag keeps a
    /// list per thread, so recording does not contend between the load threads.
    /// </summary>
    internal sealed class LatencyRecorder
    {
        public void Record(long ticks)
        {
            m_samples.Add(ticks);
        }

        public void Clear()
        {
            m_samples.Clear();
        }

        public string Percentiles()
        {
            var all = new List<long>(m_samples);
            if (all.Count == 0)
            {
                return "n=0";
            }
            all.Sort();
            double Ms(double quantile)
            {
                return all[Math.Min(all.Count - 1, (int)(quantile * all.Count))] * 1000.0 / Stopwatch.Frequency;
            }
            return FormattableString.Invariant(
                $"n={all.Count} p50={Ms(0.5):F3}ms p90={Ms(0.9):F3}ms p99={Ms(0.99):F3}ms ") +
                FormattableString.Invariant($"p999={Ms(0.999):F3}ms max={Ms(1):F3}ms");
        }

        private readonly ConcurrentBag<long> m_samples = [];
    }

    /// <summary>
    /// A load pattern driven against the server.
    /// </summary>
    internal abstract class Workload
    {
        public abstract double Operations { get; }

        public abstract string OperationName { get; }

        public abstract Task SetupAsync();

        public abstract Task RunAsync(CancellationToken ct);

        public abstract void ResetStats();

        public abstract string Report(double seconds);
    }

    /// <summary>
    /// Closed-loop Read, Write or Browse requests from every session.
    /// </summary>
    internal sealed class RequestWorkload : Workload
    {
        public RequestWorkload(string kind, ISession[] sessions, int nodes, int inflight)
        {
            m_kind = kind;
            m_sessions = sessions;
            m_nodes = nodes;
            m_inflight = inflight;
        }

        public override double Operations => Interlocked.Read(ref m_requests);

        public override string OperationName => "request";

        public override Task SetupAsync()
        {
            List<NodeId> nodes = Program.GetNodes(m_sessions[0], m_nodes, m_kind == "write" ? typeof(int) : null);
            m_read = nodes.Select(n => new ReadValueId { NodeId = n, AttributeId = Attributes.Value }).ToArray();
            m_write = nodes.Select(n => new WriteValue
            {
                NodeId = n,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(42))
            }).ToArray();
            m_browse = new[]
            {
                new BrowseDescription
                {
                    NodeId = ObjectIds.Server,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };
            Console.WriteLine(FormattableString.Invariant(
                $"{m_kind}: {nodes.Count} nodes/request, inflight {m_inflight}/session"));
            return Task.CompletedTask;
        }

        public override async Task RunAsync(CancellationToken ct)
        {
            var tasks = new List<Task>();
            foreach (ISession session in m_sessions)
            {
                for (int i = 0; i < m_inflight; i++)
                {
                    tasks.Add(Task.Run(() => LoopAsync(session, ct), CancellationToken.None));
                }
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public override void ResetStats()
        {
            Interlocked.Increment(ref m_epoch);
            m_latency.Clear();
            Interlocked.Exchange(ref m_requests, 0);
            Interlocked.Exchange(ref m_errors, 0);
        }

        public override string Report(double seconds)
        {
            long requests = Interlocked.Read(ref m_requests);
            int valuesPerRequest = m_kind == "browse" ? 1 : m_nodes;
            return FormattableString.Invariant(
                $"req/s={requests / seconds:F0} values/s={requests * valuesPerRequest / seconds:F0} ") +
                FormattableString.Invariant($"errors={Interlocked.Read(ref m_errors)} latency {m_latency.Percentiles()}");
        }

        private async Task LoopAsync(ISession session, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                long start = Stopwatch.GetTimestamp();
                int epoch = Volatile.Read(ref m_epoch);
                try
                {
                    switch (m_kind)
                    {
                        case "write":
                            await session.WriteAsync(null, m_write, ct).ConfigureAwait(false);
                            break;
                        case "browse":
                            await session.BrowseAsync(null, null, 0, m_browse, ct).ConfigureAwait(false);
                            break;
                        default:
                            await session.ReadAsync(null, 0, TimestampsToReturn.Both, m_read, ct).ConfigureAwait(false);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ServiceResultException ex)
                {
                    if (Interlocked.Increment(ref m_errors) < 5)
                    {
                        Console.WriteLine("error: " + ex.Message);
                    }
                    continue;
                }
                if (epoch == Volatile.Read(ref m_epoch))
                {
                    m_latency.Record(Stopwatch.GetTimestamp() - start);
                    Interlocked.Increment(ref m_requests);
                }
            }
        }

        private readonly string m_kind;
        private readonly ISession[] m_sessions;
        private readonly int m_nodes;
        private readonly int m_inflight;
        private readonly LatencyRecorder m_latency = new();
        private int m_epoch;
        private long m_requests;
        private long m_errors;
        private ArrayOf<ReadValueId> m_read;
        private ArrayOf<WriteValue> m_write;
        private ArrayOf<BrowseDescription> m_browse;
    }

    /// <summary>
    /// Data-change subscriptions on every session and one writer changing the monitored values.
    /// The written value is a counter, so each notification maps back to the time of its write.
    /// </summary>
    internal sealed class SubscribeWorkload : Workload
    {
        public SubscribeWorkload(ISession[] sessions, int nodes, int subscriptions, int publishingInterval, int writeInterval)
        {
            m_sessions = sessions;
            m_nodes = nodes;
            m_subscriptions = subscriptions;
            m_publishingInterval = publishingInterval;
            m_writeInterval = writeInterval;
        }

        public override double Operations => Interlocked.Read(ref m_notifications);

        public override string OperationName => "notification";

        public override async Task SetupAsync()
        {
            m_nodeIds = Program.GetNodes(m_sessions[0], m_nodes, typeof(int)).ToArray();
            var setupTime = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                m_sessions,
                new ParallelOptions { MaxDegreeOfParallelism = 16 },
                async (session, ct) =>
                {
                    for (int i = 0; i < m_subscriptions; i++)
                    {
                        await CreateSubscriptionAsync(session, ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"sub: {m_sessions.Length} sessions x {m_subscriptions} subs x {m_nodeIds.Count} items, ") +
                FormattableString.Invariant(
                $"pub {m_publishingInterval} ms, write every {m_writeInterval} ms; setup {setupTime.ElapsedMilliseconds} ms"));
        }

        public override async Task RunAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(m_writeInterval));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                int counter = Interlocked.Increment(ref m_counter);
                var values = new WriteValue[m_nodeIds.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = new WriteValue
                    {
                        NodeId = m_nodeIds[i],
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(counter))
                    };
                }
                Volatile.Write(ref m_writeTicks[counter & 0xFFFF], Stopwatch.GetTimestamp());
                await m_sessions[0].WriteAsync(null, values, ct).ConfigureAwait(false);
                Interlocked.Increment(ref m_writes);
            }
        }

        public override void ResetStats()
        {
            m_latency.Clear();
            Interlocked.Exchange(ref m_notifications, 0);
            Interlocked.Exchange(ref m_writes, 0);
        }

        public override string Report(double seconds)
        {
            long notifications = Interlocked.Read(ref m_notifications);
            long expected = Interlocked.Read(ref m_writes) * m_sessions.Length * m_subscriptions * m_nodeIds.Count;
            double ratio = expected == 0 ? 0 : (double)notifications / expected;
            return FormattableString.Invariant(
                $"notif/s={notifications / seconds:F0} received/expected={ratio:P1} ") +
                FormattableString.Invariant($"write->notify latency {m_latency.Percentiles()}");
        }

        private async Task CreateSubscriptionAsync(ISession session, CancellationToken ct)
        {
#pragma warning disable CA2000 // ownership transfers to the session in AddSubscription; disposed below on failure before that
            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = m_publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                FastDataChangeCallback = OnDataChange
            };
#pragma warning restore CA2000
            try
            {
                foreach (NodeId nodeId in m_nodeIds)
                {
                    subscription.AddItem(new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = nodeId,
                        AttributeId = Attributes.Value,
                        MonitoringMode = MonitoringMode.Reporting,
                        SamplingInterval = 0,
                        QueueSize = 1
                    });
                }
                // the session owns and disposes the subscription from here on
                session.AddSubscription(subscription);
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
            await subscription.CreateAsync(ct).ConfigureAwait(false);
        }

        private void OnDataChange(Subscription subscription, DataChangeNotification notification, ArrayOf<string> stringTable)
        {
            long now = Stopwatch.GetTimestamp();
            int count = 0;
            foreach (MonitoredItemNotification item in notification.MonitoredItems)
            {
                if (item.Value.WrappedValue.TryGetValue(out int counter) && counter > 0)
                {
                    long written = Volatile.Read(ref m_writeTicks[counter & 0xFFFF]);
                    if (written != 0)
                    {
                        m_latency.Record(now - written);
                    }
                }
                count++;
            }
            Interlocked.Add(ref m_notifications, count);
        }

        private readonly ISession[] m_sessions;
        private readonly int m_nodes;
        private readonly int m_subscriptions;
        private readonly int m_publishingInterval;
        private readonly int m_writeInterval;
        private readonly long[] m_writeTicks = new long[1 << 16];
        private readonly LatencyRecorder m_latency = new();
        private long m_notifications;
        private long m_writes;
        private int m_counter;
        private ArrayOf<NodeId> m_nodeIds;
    }
}
