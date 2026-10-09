// Server load / profiling harness. Not part of the product; see perf/README.md.
// Modes:
//   server  --port P [--diag 0|1] [--audit 0|1] [--threads N]
//           Starts a ReferenceServer and reads commands from stdin: mark | report | quit
//   run     --scenario read|write|sub|browse [--sessions N] [--nodes K] [--inflight I]
//           [--duration S] [--warmup S] [--security none|sign|encrypt] [--diag 0|1] [--audit 0|1]
//           [--subs S] [--pub ms] [--write-interval ms] [--trace file] [--trace-profile p]
//           Spawns the server child, drives the load and prints a report of client latency
//           and server-side CPU / GC / allocation deltas for the measurement window.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

internal static class Program
{
    private static readonly Dictionary<string, string> s_args = new(StringComparer.OrdinalIgnoreCase);

    private static string Arg(string name, string def) => s_args.TryGetValue(name, out string? v) ? v : def;
    private static int ArgInt(string name, int def) => int.Parse(Arg(name, def.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    public static async Task<int> Main(string[] args)
    {
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                string key = args[i][2..];
                string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "1";
                s_args[key] = value;
            }
        }
        string mode = args.Length > 0 ? args[0] : "run";
        return mode switch
        {
            "server" => await RunServerAsync().ConfigureAwait(false),
            _ => await RunDriverAsync(args).ConfigureAwait(false)
        };
    }

    // ------------------------------------------------------------------ server

    private static ITelemetryContext CreateTelemetry(LogLevel level) =>
        DefaultTelemetry.Create(b => b.SetMinimumLevel(level).AddSimpleConsole(o => o.SingleLine = true));

    private static async Task<int> RunServerAsync()
    {
        int port = ArgInt("port", 62555);
        bool diag = ArgInt("diag", 0) != 0;
        bool audit = ArgInt("audit", 0) != 0;
        ITelemetryContext telemetry = CreateTelemetry(LogLevel.Error);

        string pkiRoot = Path.Combine(Path.GetTempPath(), "uaperf-pki-server");
        var application = new ApplicationInstance(telemetry)
        {
            ApplicationName = "PerfReferenceServer",
            ApplicationType = ApplicationType.Server
        };
        string endpointUrl = $"opc.tcp://localhost:{port}/PerfReferenceServer";
        ArrayOf<CertificateIdentifier> certs = ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
            "CN=PerfReferenceServer, O=OPC Foundation, DC=localhost", CertificateStoreType.Directory, pkiRoot);
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
            .SetDiagnosticsEnabled(diag)
            .SetAuditingEnabled(audit)
            .SetShutdownDelay(0)
            .AddSecurityConfiguration(certs, pkiRoot)
            .SetAutoAcceptUntrustedCertificates(true)
            .CreateAsync().ConfigureAwait(false);
        config.ServerConfiguration!.MaxSessionCount = 2000;
        config.ServerConfiguration.MaxSubscriptionCount = 20000;
        config.ServerConfiguration.MaxFailedAuthenticationAttempts = 0;
        config.ServerConfiguration.MaxRequestThreadCount = ArgInt("threads", 200);
        config.ServerConfiguration.MinRequestThreadCount = 50;

        if (!await application.CheckApplicationInstanceCertificatesAsync(true, CertificateFactory.DefaultLifeTime).ConfigureAwait(false))
        {
            Console.WriteLine("ERROR cert");
            return 1;
        }

        var server = new ReferenceServer(telemetry);
        server.TransportBindings = TestTransportBindings.WithAllSchemes();
        await application.StartAsync(server).ConfigureAwait(false);
        Console.WriteLine($"READY {endpointUrl} {Environment.ProcessId}");

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
                    Console.WriteLine("MARKED");
                    break;
                case "report":
                    Console.WriteLine("REPORT " + ServerStats.Capture().Delta(mark));
                    break;
                case "quit":
                    await server.StopAsync().ConfigureAwait(false);
                    return 0;
            }
        }
        return 0;
    }

    private sealed record ServerStats(long Alloc, int G0, int G1, int G2, TimeSpan Pause, TimeSpan Cpu, long Contention, long Stamp, int Threads, long WorkingSet, long Heap)
    {
        public static ServerStats Capture()
        {
            using var p = Process.GetCurrentProcess();
            return new ServerStats(
                GC.GetTotalAllocatedBytes(true), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
                GC.GetTotalPauseDuration(), p.TotalProcessorTime, Monitor.LockContentionCount, Stopwatch.GetTimestamp(),
                ThreadPool.ThreadCount, p.WorkingSet64, GC.GetGCMemoryInfo().HeapSizeBytes);
        }

        public string Delta(ServerStats m)
        {
            double sec = Stopwatch.GetElapsedTime(m.Stamp, Stamp).TotalSeconds;
            return string.Create(CultureInfo.InvariantCulture,
                $"sec={sec:F2} allocMB={(Alloc - m.Alloc) / 1048576.0:F1} gen0={G0 - m.G0} gen1={G1 - m.G1} gen2={G2 - m.G2} pauseMs={(Pause - m.Pause).TotalMilliseconds:F0} cpuMs={(Cpu - m.Cpu).TotalMilliseconds:F0} cpuCores={(Cpu - m.Cpu).TotalSeconds / sec:F2} contention={Contention - m.Contention} threads={Threads} wsMB={WorkingSet / 1048576} heapMB={Heap / 1048576}");
        }
    }

    // ------------------------------------------------------------------ driver

    private static async Task<int> RunDriverAsync(string[] args)
    {
        string scenario = Arg("scenario", "read");
        int sessionsCount = ArgInt("sessions", 20);
        int durationSec = ArgInt("duration", 20);
        int warmupSec = ArgInt("warmup", 10);
        int port = ArgInt("port", 62555 + Environment.ProcessId % 1000);
        string security = Arg("security", "none");

        string exe = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(exe, $"server --port {port} --diag {Arg("diag", "0")} --audit {Arg("audit", "0")} --threads {Arg("threads", "200")}")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            string k = (string)kv.Key;
            if (k.StartsWith("SERVER_", StringComparison.Ordinal))
            {
                psi.Environment[k[7..]] = (string?)kv.Value;
            }
        }
        using Process server = Process.Start(psi)!;
        var serverLines = new BlockingCollection<string>();
        _ = Task.Run(() =>
        {
            string? l;
            while ((l = server.StandardOutput.ReadLine()) != null)
            {
                if (l.StartsWith("READY", StringComparison.Ordinal) || l.StartsWith("REPORT", StringComparison.Ordinal) || l.StartsWith("MARKED", StringComparison.Ordinal) || l.StartsWith("ERROR", StringComparison.Ordinal))
                {
                    serverLines.Add(l);
                }
                else if (s_args.ContainsKey("verbose"))
                {
                    Console.WriteLine("[server] " + l);
                }
            }
            serverLines.CompleteAdding();
        });
        string ready = serverLines.Take();
        if (!ready.StartsWith("READY", StringComparison.Ordinal))
        {
            Console.WriteLine(ready);
            return 1;
        }
        string url = ready.Split(' ')[1];
        Console.WriteLine($"server pid={server.Id} url={url}");

        ITelemetryContext telemetry = CreateTelemetry(LogLevel.Error);
        var client = new ClientFixture(telemetry) { SessionTimeout = 60000, OperationTimeout = 60000 };
        await client.LoadClientConfigurationAsync(Path.Combine(Path.GetTempPath(), "uaperf-pki-client"), "PerfClient").ConfigureAwait(false);
        string policy = security == "none" ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256;
        ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(new Uri(url)).ConfigureAwait(false);
        MessageSecurityMode wantedMode = security switch
        {
            "sign" => MessageSecurityMode.Sign,
            "encrypt" => MessageSecurityMode.SignAndEncrypt,
            _ => MessageSecurityMode.None
        };
        var filtered = new List<EndpointDescription>();
        foreach (EndpointDescription ep in endpoints)
        {
            if (ep.SecurityPolicyUri == policy && ep.SecurityMode == wantedMode)
            {
                filtered.Add(ep);
            }
        }

        var sessions = new ISession[sessionsCount];
        var connectSw = Stopwatch.StartNew();
        await Parallel.ForAsync(0, sessionsCount, new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (i, ct) =>
            sessions[i] = await client.ConnectAsync(new Uri(url), policy, filtered).ConfigureAwait(false)).ConfigureAwait(false);
        Console.WriteLine($"connected {sessionsCount} sessions ({security}) in {connectSw.ElapsedMilliseconds} ms");

        Workload workload = scenario switch
        {
            "sub" => new SubscribeWorkload(sessions, ArgInt("nodes", 100), ArgInt("subs", 5), ArgInt("pub", 100), ArgInt("write-interval", 100)),
            _ => new RequestWorkload(scenario, sessions, ArgInt("nodes", 100), ArgInt("inflight", 1))
        };
        await workload.SetupAsync().ConfigureAwait(false);

        using var stop = new CancellationTokenSource();
        Task run = workload.RunAsync(stop.Token);
        await Task.Delay(TimeSpan.FromSeconds(warmupSec)).ConfigureAwait(false);

        Process? tracer = null;
        string? trace = s_args.TryGetValue("trace", out string? t) ? t : null;
        if (trace != null)
        {
            string profile = Arg("trace-profile", "cpu");
            string providers = profile switch
            {
                // allocation sampling (GCAllocationTick ~ every 100KB) + GC events
                "alloc" => "--providers Microsoft-Windows-DotNETRuntime:0x1:5 --clrevents gc --clreventlevel verbose",
                "gc" => "--profile gc-verbose",
                "contention" => "--providers Microsoft-Windows-DotNETRuntime:0x4000:4",
                _ => "--profile dotnet-sampled-thread-time"
            };
            tracer = Process.Start(new ProcessStartInfo("dotnet-trace", $"collect --process-id {server.Id} {providers} -o \"{trace}\" --duration 00:00:{durationSec:D2}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            await Task.Delay(2000).ConfigureAwait(false);
        }

        server.StandardInput.WriteLine("mark");
        Take(serverLines, "MARKED");
        workload.ResetStats();
        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(durationSec)).ConfigureAwait(false);
        server.StandardInput.WriteLine("report");
        string report = Take(serverLines, "REPORT");
        double elapsed = sw.Elapsed.TotalSeconds;
        string clientReport = workload.Report(elapsed);
        stop.Cancel();
        try
        {
            await run.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (tracer != null)
        {
            await tracer.WaitForExitAsync().ConfigureAwait(false);
            Console.WriteLine("trace: " + trace);
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"RESULT scenario={scenario} security={security} sessions={sessionsCount} {string.Join(' ', args.Skip(1))}\n");
        sb.Append("  client: ").Append(clientReport).Append('\n');
        sb.Append("  server: ").Append(report[7..]).Append('\n');
        double ops = workload.Operations;
        if (ops > 0)
        {
            ServerReport sr = ServerReport.Parse(report[7..]);
            sb.Append(CultureInfo.InvariantCulture, $"  per-op: allocB={sr.AllocMb * 1048576 / ops:F0} cpuUs={sr.CpuMs * 1000 / ops:F1}  ({workload.OperationName})\n");
        }
        Console.Write(sb.ToString());
        if (s_args.TryGetValue("out", out string? outFile))
        {
            File.AppendAllText(outFile, sb.ToString());
        }

        await Parallel.ForEachAsync(sessions, async (s, ct) =>
        {
            try
            {
                await s.CloseAsync(ct).ConfigureAwait(false);
            }
            catch
            {
            }
            s.Dispose();
        }).ConfigureAwait(false);
        server.StandardInput.WriteLine("quit");
        if (!server.WaitForExit(10000))
        {
            server.Kill();
        }
        return 0;
    }

    private static string Take(BlockingCollection<string> lines, string prefix)
    {
        while (true)
        {
            string l = lines.Take();
            if (l.StartsWith(prefix, StringComparison.Ordinal))
            {
                return l;
            }
        }
    }

    private sealed record ServerReport(double AllocMb, double CpuMs)
    {
        public static ServerReport Parse(string s)
        {
            var d = s.Split(' ').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            return new ServerReport(double.Parse(d["allocMB"], CultureInfo.InvariantCulture), double.Parse(d["cpuMs"], CultureInfo.InvariantCulture));
        }
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
            foreach (ExpandedNodeId e in ids)
            {
                if (result.Count == count)
                {
                    return result;
                }
                result.Add(ExpandedNodeId.ToNodeId(e, session.NamespaceUris));
            }
        }
        return result;
    }
}

internal abstract class Workload
{
    public abstract Task SetupAsync();
    public abstract Task RunAsync(CancellationToken ct);
    public abstract void ResetStats();
    public abstract string Report(double seconds);
    public abstract double Operations { get; }
    public abstract string OperationName { get; }

    protected static string Percentiles(List<long> ticksList)
    {
        if (ticksList.Count == 0)
        {
            return "n=0";
        }
        long[] a = [.. ticksList];
        Array.Sort(a);
        double Ms(double q) => a[Math.Min(a.Length - 1, (int)(q * a.Length))] * 1000.0 / Stopwatch.Frequency;
        return string.Create(CultureInfo.InvariantCulture,
            $"n={a.Length} p50={Ms(0.5):F3}ms p90={Ms(0.9):F3}ms p99={Ms(0.99):F3}ms p999={Ms(0.999):F3}ms max={Ms(1):F3}ms");
    }
}

internal sealed class RequestWorkload : Workload
{
    private readonly string m_kind;
    private readonly ISession[] m_sessions;
    private readonly int m_nodes;
    private readonly int m_inflight;
    private readonly ConcurrentBag<List<long>> m_lat = new();
    private readonly ThreadLocal<List<long>> m_local;
    private volatile int m_epoch;
    private long m_requests;
    private long m_errors;
    private ArrayOf<ReadValueId> m_read;
    private ArrayOf<WriteValue> m_write;
    private ArrayOf<BrowseDescription> m_browse;

    public RequestWorkload(string kind, ISession[] sessions, int nodes, int inflight)
    {
        m_kind = kind;
        m_sessions = sessions;
        m_nodes = nodes;
        m_inflight = inflight;
        m_local = new ThreadLocal<List<long>>(() =>
        {
            var l = new List<long>(1 << 16);
            m_lat.Add(l);
            return l;
        }, false);
    }

    public override double Operations => Interlocked.Read(ref m_requests);
    public override string OperationName => "request";

    public override Task SetupAsync()
    {
        List<NodeId> nodes = Program.GetNodes(m_sessions[0], m_nodes, m_kind == "write" ? typeof(int) : null);
        m_read = nodes.Select(n => new ReadValueId { NodeId = n, AttributeId = Attributes.Value }).ToArray();
        m_write = nodes.Select(n => new WriteValue { NodeId = n, AttributeId = Attributes.Value, Value = new DataValue(new Variant(42)) }).ToArray();
        m_browse = new[] { new BrowseDescription { NodeId = Opc.Ua.ObjectIds.Server, BrowseDirection = BrowseDirection.Forward, ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences, IncludeSubtypes = true, ResultMask = (uint)BrowseResultMask.All } };
        Console.WriteLine($"{m_kind}: {nodes.Count} nodes/request, inflight {m_inflight}/session");
        return Task.CompletedTask;
    }

    public override async Task RunAsync(CancellationToken ct)
    {
        var tasks = new List<Task>();
        foreach (ISession s in m_sessions)
        {
            for (int i = 0; i < m_inflight; i++)
            {
                tasks.Add(Task.Run(() => LoopAsync(s, ct), CancellationToken.None));
            }
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task LoopAsync(ISession s, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();
            int epoch = m_epoch;
            try
            {
                switch (m_kind)
                {
                    case "write":
                        await s.WriteAsync(null, m_write, ct).ConfigureAwait(false);
                        break;
                    case "browse":
                        await s.BrowseAsync(null, null, 0, m_browse, ct).ConfigureAwait(false);
                        break;
                    default:
                        await s.ReadAsync(null, 0, TimestampsToReturn.Both, m_read, ct).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (Interlocked.Increment(ref m_errors) < 5)
                {
                    Console.WriteLine("error: " + ex.Message);
                }
                continue;
            }
            long el = Stopwatch.GetTimestamp() - start;
            if (epoch == m_epoch)
            {
                List<long> l = m_local.Value!;
                lock (l)
                {
                    l.Add(el);
                }
                Interlocked.Increment(ref m_requests);
            }
        }
    }

    public override void ResetStats()
    {
        m_epoch++;
        foreach (List<long> l in m_lat)
        {
            lock (l)
            {
                l.Clear();
            }
        }
        Interlocked.Exchange(ref m_requests, 0);
        Interlocked.Exchange(ref m_errors, 0);
    }

    public override string Report(double seconds)
    {
        var all = new List<long>();
        foreach (List<long> l in m_lat)
        {
            lock (l)
            {
                all.AddRange(l);
            }
        }
        long req = Interlocked.Read(ref m_requests);
        return string.Create(CultureInfo.InvariantCulture,
            $"req/s={req / seconds:F0} values/s={req * (m_kind == "browse" ? 1 : m_nodes) / seconds:F0} errors={m_errors} latency {Percentiles(all)}");
    }
}

internal sealed class SubscribeWorkload : Workload
{
    private readonly ISession[] m_sessions;
    private readonly int m_nodes;
    private readonly int m_subs;
    private readonly int m_pub;
    private readonly int m_writeInterval;
    private readonly long[] m_writeTicks = new long[1 << 16];
    private readonly ConcurrentBag<List<long>> m_lat = new();
    private readonly ThreadLocal<List<long>> m_local;
    private long m_notifications;
    private long m_writes;
    private int m_counter;
    private ISession? m_writer;
    private ArrayOf<NodeId> m_nodeIds;

    public SubscribeWorkload(ISession[] sessions, int nodes, int subs, int pub, int writeInterval)
    {
        m_sessions = sessions;
        m_nodes = nodes;
        m_subs = subs;
        m_pub = pub;
        m_writeInterval = writeInterval;
        m_local = new ThreadLocal<List<long>>(() =>
        {
            var l = new List<long>(1 << 16);
            m_lat.Add(l);
            return l;
        }, false);
    }

    public override double Operations => Interlocked.Read(ref m_notifications);
    public override string OperationName => "notification";

    public override async Task SetupAsync()
    {
        m_nodeIds = Program.GetNodes(m_sessions[0], m_nodes, typeof(int)).ToArray();
        m_writer = m_sessions[0];
        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(m_sessions, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (session, ct) =>
        {
            for (int j = 0; j < m_subs; j++)
            {
                var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = m_pub,
                    KeepAliveCount = 10,
                    LifetimeCount = 100
                };
                foreach (NodeId n in m_nodeIds)
                {
                    subscription.AddItem(new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = n,
                        AttributeId = Attributes.Value,
                        MonitoringMode = MonitoringMode.Reporting,
                        SamplingInterval = 0,
                        QueueSize = 1
                    });
                }
                subscription.FastDataChangeCallback = (sub, notification, _) =>
                {
                    long now = Stopwatch.GetTimestamp();
                    List<long> l = m_local.Value!;
                    int count = 0;
                    lock (l)
                    {
                        foreach (MonitoredItemNotification item in notification.MonitoredItems)
                        {
                            if (item.Value.WrappedValue.TryGetValue(out int v) && v > 0)
                            {
                                long w = Volatile.Read(ref m_writeTicks[v & 0xFFFF]);
                                if (w != 0)
                                {
                                    l.Add(now - w);
                                }
                            }
                            count++;
                        }
                    }
                    Interlocked.Add(ref m_notifications, count);
                };
                session.AddSubscription(subscription);
                await subscription.CreateAsync(ct).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
        Console.WriteLine($"sub: {m_sessions.Length} sessions x {m_subs} subs x {m_nodeIds.Count} items = {m_sessions.Length * m_subs * m_nodeIds.Count} items, pub {m_pub} ms, write every {m_writeInterval} ms; setup {sw.ElapsedMilliseconds} ms");
    }

    public override async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(m_writeInterval));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            int v = Interlocked.Increment(ref m_counter);
            var values = new WriteValue[m_nodeIds.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new WriteValue { NodeId = m_nodeIds[i], AttributeId = Attributes.Value, Value = new DataValue(new Variant(v)) };
            }
            Volatile.Write(ref m_writeTicks[v & 0xFFFF], Stopwatch.GetTimestamp());
            await m_writer!.WriteAsync(null, values, ct).ConfigureAwait(false);
            Interlocked.Increment(ref m_writes);
        }
    }

    public override void ResetStats()
    {
        foreach (List<long> l in m_lat)
        {
            lock (l)
            {
                l.Clear();
            }
        }
        Interlocked.Exchange(ref m_notifications, 0);
        Interlocked.Exchange(ref m_writes, 0);
    }

    public override string Report(double seconds)
    {
        var all = new List<long>();
        foreach (List<long> l in m_lat)
        {
            lock (l)
            {
                all.AddRange(l);
            }
        }
        long n = Interlocked.Read(ref m_notifications);
        long expected = Interlocked.Read(ref m_writes) * m_sessions.Length * m_subs * m_nodeIds.Count;
        return string.Create(CultureInfo.InvariantCulture,
            $"notif/s={n / seconds:F0} received/expected={(expected == 0 ? 0 : (double)n / expected):P1} write->notify latency {Percentiles(all)}");
    }
}
