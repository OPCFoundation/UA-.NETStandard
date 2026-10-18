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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Drives a server with the native open62541 workers of the o6-automation opcua-benchmarks
    /// (https://github.com/o6-automation/opcua-benchmarks), so that the .NET server and other
    /// stacks are measured with the same client as in that benchmark.
    /// </summary>
    /// <remarks>
    /// <c>native-read</c>: the fixed-load scalar Read test of <c>throughput/open62541/client.c</c>:
    /// <c>--clients</c> worker processes keep <c>--inflight</c> single-value Reads of
    /// ns=1;i=1001..1100 in flight, start together and report the aggregate rate.
    /// <c>native-counters</c>: the subscription capacity test of <c>subscription/native/client.c</c>:
    /// one subscription with a monitored item per counter (queue size one); every item must
    /// reach 99% of the samples its sampling interval calls for.
    /// The server is the harness child in contract mode (<c>server --contract 1</c>), configured as the
    /// benchmark's .NET worker for UA-.NETStandard 2.0.1 is: <see cref="AsyncCustomNodeManager"/> node managers
    /// (<c>--async-node-manager 0</c> for <see cref="CustomNodeManager2"/>), counters incremented under a lock
    /// (<c>--counter-lock 0</c> for an interlocked increment), Server GC, and a request queue of
    /// clients x in flight + 3. Or the server is any
    /// executable given by <c>--server-exe</c>/<c>--server-args</c>, for example the open62541
    /// server of that benchmark (<c>{port}</c>, <c>{items}</c>, <c>{sampling}</c> and <c>{pki}</c>, the
    /// directory with the DER identities <c>server-cert.der</c>/<c>server-key.der</c>/<c>client-cert.der</c>,
    /// are replaced).
    /// </remarks>
    internal static class NativeWorkerBenchmark
    {
        /// <summary>
        /// The server and namespace URI of the benchmark contract (namespace index 1). The value is
        /// fixed by the external workers: the contract puts its nodes in the namespace of this server
        /// URI, and the workers address them as ns=1.
        /// </summary>
        public const string ContractServerUri = "urn:o6:benchmark:server";

        // The protocol of the external workers, which define these names; not ours to rename.

        /// <summary>
        /// Environment variable that selects the security policy of a worker (SignAndEncrypt when set).
        /// </summary>
        public const string WorkerSecurityPolicyVariable = "O6_BENCHMARK_SECURITY_POLICY";

        /// <summary>
        /// Environment variable with the path of the DER certificate of a worker.
        /// </summary>
        public const string WorkerCertificateVariable = "O6_BENCHMARK_CERTIFICATE";

        /// <summary>
        /// Environment variable with the path of the DER private key of a worker.
        /// </summary>
        public const string WorkerPrivateKeyVariable = "O6_BENCHMARK_PRIVATE_KEY";

        /// <summary>
        /// The line a Read worker writes to stdout once it is connected and warmed up.
        /// </summary>
        public const string WorkerReadyToken = "O6_BENCHMARK_READY";

        /// <summary>
        /// Application URI prefix of the worker identities, as the certificate script of the
        /// benchmark writes them; the workers announce these application URIs.
        /// </summary>
        public const string WorkerApplicationUriPrefix = "urn:o6:benchmark:";

        /// <summary>
        /// Subject prefix of the worker identities, as the certificate script of the benchmark writes them.
        /// </summary>
        public const string WorkerSubjectPrefix = "CN=o6 benchmark ";

        public const uint FirstNodeId = 1001;

        public const int NodeCount = 100;

        public static bool IsScenario(string scenario)
        {
            return scenario is "native-read" or "native-counters";
        }

        public static async Task<int> RunAsync(string[] args)
        {
            string scenario = Program.Arg("scenario", "native-read");
            int port = Program.ArgInt("port", 48400 + (Environment.ProcessId % 1000));
            bool encrypt = Program.Arg("security", "none") == "encrypt";
            if (encrypt && scenario == "native-counters")
            {
                // the subscription worker gets no identity and connects without security, so the
                // run would be labelled encrypt but measure an unsecured channel
                Console.Error.WriteLine("--security encrypt is not supported for native-counters");
                return 2;
            }
            string pki = Directory.CreateTempSubdirectory("uaperf-native-").FullName;
            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (encrypt)
                {
                    environment[WorkerSecurityPolicyVariable] = "Basic256Sha256";
                    WriteIdentity(pki, "client");
                    WriteIdentity(pki, "server");
                }
                using ServerHandle server = await ServerHandle.StartAsync(scenario, port, encrypt, pki).ConfigureAwait(false);
                var result = new StringBuilder();
                result.Append(CultureInfo.InvariantCulture, $"RESULT scenario={scenario} security={(encrypt ? "encrypt" : "none")} ")
                    .Append(string.Join(' ', args.Skip(1)))
                    .Append('\n');
                bool ok = scenario == "native-counters"
                    ? await RunCountersAsync(server, port, environment, result).ConfigureAwait(false)
                    : await RunReadAsync(server, port, environment, pki, result).ConfigureAwait(false);
                Console.Write(result.ToString());
                if (Program.TryGetArg("out", out string? outFile))
                {
                    await File.AppendAllTextAsync(outFile!, result.ToString()).ConfigureAwait(false);
                }
                await server.StopAsync().ConfigureAwait(false);
                return ok ? 0 : 1;
            }
            finally
            {
                try
                {
                    Directory.Delete(pki, true);
                }
                catch (IOException)
                {
                    // a worker may still hold a file for a moment
                }
            }
        }

        private static async Task<bool> RunReadAsync(
            ServerHandle server,
            int port,
            Dictionary<string, string> environment,
            string pki,
            StringBuilder result)
        {
            string exe = Program.Arg("native-client", "client.exe");
            int clients = Program.ArgInt("clients", 10);
            int inflight = Program.ArgInt("inflight", 100);
            int iterations = Program.ArgInt("iterations", 100000);
            int warmup = Program.ArgInt("warmup-calls", 10000);
            var workers = new List<Worker>();
            try
            {
                for (int i = 0; i < clients; i++)
                {
                    workers.Add(Worker.StartClient(
                        exe,
                        FormattableString.Invariant(
                            $"--endpoint opc.tcp://127.0.0.1:{port} --iterations {iterations} --warmup {warmup} --samples 1 ") +
                        FormattableString.Invariant(
                            $"--worker async --operation read --max-outstanding {inflight} --seed {i + 1}"),
                        WithIdentity(environment, pki, "client")));
                }
                foreach (Worker worker in workers)
                {
                    worker.WaitFor(l => l == WorkerReadyToken, TimeSpan.FromSeconds(120));
                }

                using Process? tracer = server.StartTracer();
                await server.MarkAsync().ConfigureAwait(false);
                TimeSpan clientCpu = workers.Aggregate(TimeSpan.Zero, (sum, w) => sum + w.Cpu);
                foreach (Worker worker in workers)
                {
                    worker.Send(string.Empty);
                }
                var reports = new List<JsonElement>();
                foreach (Worker worker in workers)
                {
                    string line = worker.WaitFor(l => l.StartsWith('{'), TimeSpan.FromSeconds(600));
                    reports.Add(JsonDocument.Parse(line).RootElement);
                }
                clientCpu = workers.Aggregate(TimeSpan.Zero, (sum, w) => sum + w.Cpu) - clientCpu;
                string serverReport = await server.ReportAsync().ConfigureAwait(false);
                if (tracer != null)
                {
                    await tracer.WaitForExitAsync().ConfigureAwait(false);
                }

                long start = reports.Min(r => r.GetProperty("start_ns").GetInt64());
                long end = reports.Max(r => r.GetProperty("end_ns").GetInt64());
                long operations = reports.Sum(r => r.GetProperty("operations").GetInt64());
                double seconds = (end - start) / 1e9;
                result.Append(CultureInfo.InvariantCulture,
                    $"  client: req/s={operations / seconds:F0} n={operations} sec={seconds:F2} clients={clients} inflight={inflight} ")
                    .Append(CultureInfo.InvariantCulture,
                    $"clientCpuCores={clientCpu.TotalSeconds / seconds:F2}\n")
                    .Append("  server: ").Append(serverReport).Append('\n');
                AppendPerOp(result, serverReport, operations, clientCpu, "request");
                return true;
            }
            finally
            {
                foreach (Worker worker in workers)
                {
                    worker.Dispose();
                }
            }
        }

        private static async Task<bool> RunCountersAsync(
            ServerHandle server,
            int port,
            Dictionary<string, string> environment,
            StringBuilder result)
        {
            string exe = Program.Arg("native-client", "sub_client.exe");
            int items = Program.ArgInt("nodes", 1);
            int sampling = Program.ArgInt("sampling", 10);
            int publishing = Program.ArgInt("pub", 1000);
            using Worker client = Worker.StartClient(
                exe,
                FormattableString.Invariant($"{port} {items} {sampling} client {publishing}"),
                environment);
            string ready = client.WaitFor(l => l.Contains("\"ready\"", StringComparison.Ordinal), TimeSpan.FromSeconds(300));
            Console.WriteLine("client " + ready);

            // measurement_window(): at least 1000 samples and 5 publishes, plus 10% warm-up
            long intervals = Math.Max(5, ((1000L * sampling) + publishing - 1) / publishing);
            long warmupIntervals = (intervals + 9) / 10;
            const int SettleMs = 2000;
            const int ArmLeadMs = 500;
            // attach the tracer before the window is armed, so that marking is not delayed; by
            // default it records the settle time, the lead-in and the window plus 2 s
            long traceSeconds = (SettleMs + ArmLeadMs + ((warmupIntervals + intervals) * publishing) + 999) / 1000 + 2;
            using Process? tracer = server.StartTracer(Program.ArgInt("duration", (int)traceSeconds));
            // the benchmark coordinator settles the subscription for 2 s before arming
            await Task.Delay(SettleMs).ConfigureAwait(false);

            long start = NowNs() + (ArmLeadMs * 1_000_000L);
            long measuredStart = start + (warmupIntervals * publishing * 1_000_000L);
            long measuredEnd = start + ((warmupIntervals + intervals) * publishing * 1_000_000L);
            string arm = FormattableString.Invariant(
                $"arm {start} {measuredEnd} {measuredStart} {measuredEnd} {publishing * 1_000_000L}");
            client.Send(arm);
            client.WaitFor(l => l.Contains("\"armed\"", StringComparison.Ordinal), TimeSpan.FromSeconds(2));
            server.Arm(arm);

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (measuredStart - NowNs()) / 1e6))).ConfigureAwait(false);
            await server.MarkAsync().ConfigureAwait(false);
            TimeSpan clientCpu = client.Cpu;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (measuredEnd - NowNs()) / 1e6))).ConfigureAwait(false);
            clientCpu = client.Cpu - clientCpu;
            string serverReport = await server.ReportAsync().ConfigureAwait(false);
            string line = client.WaitFor(
                l => l.Contains("\"result\"", StringComparison.Ordinal),
                TimeSpan.FromMilliseconds(((measuredEnd - start) / 1e6) + 15000));
            if (tracer != null)
            {
                await tracer.WaitForExitAsync().ConfigureAwait(false);
            }

            JsonElement root = JsonDocument.Parse(line).RootElement;
            // counts: how far each counter advanced, i.e. the samples the server took
            ulong[] counts = root.GetProperty("counts").EnumerateArray().Select(e => e.GetUInt64()).ToArray();
            double expected = (measuredEnd - measuredStart) / 1e6 / sampling;
            double min = counts.Min() / expected;
            double mean = counts.Average(c => (double)c) / expected;
            int below = counts.Count(c => c / expected < 0.99);
            JsonElement delivery = root.GetProperty("delivery");
            double samples = counts.Sum(c => (double)c);
            double seconds = (measuredEnd - measuredStart) / 1e9;
            result.Append(CultureInfo.InvariantCulture,
                $"  client: samples/s={samples / seconds:F0} ");
            if (delivery.TryGetProperty("notifications", out JsonElement notifications) &&
                notifications.ValueKind == JsonValueKind.Number)
            {
                // with queue size 1 only the last sample per publishing interval is delivered
                result.Append(CultureInfo.InvariantCulture, $"notif/s={notifications.GetDouble() / seconds:F0} ");
            }
            result.Append(CultureInfo.InvariantCulture,
                $"items={items} sampling={sampling}ms pub={publishing}ms ")
                .Append(CultureInfo.InvariantCulture,
                $"progressMin={min:P2} progressMean={mean:P2} itemsBelow99={below} ")
                .Append(CultureInfo.InvariantCulture,
                $"maxGapMs={delivery.GetProperty("max_gap_ms").GetDouble():F1} {(below == 0 ? "PASS" : "FAIL")}\n")
                .Append("  server: ").Append(serverReport).Append('\n');
            AppendPerOp(result, serverReport, samples, clientCpu, "sample");
            client.Send("quit");
            return below == 0;
        }

        private static void AppendPerOp(StringBuilder result, string serverReport, double operations, TimeSpan clientCpu, string unit)
        {
            if (operations <= 0)
            {
                return;
            }
            Dictionary<string, string> values = serverReport
                .Split(' ')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
            double allocatedMb = values.TryGetValue("allocMB", out string? a) ? double.Parse(a, CultureInfo.InvariantCulture) : 0;
            double cpuMs = double.Parse(values["cpuMs"], CultureInfo.InvariantCulture);
            result.Append(CultureInfo.InvariantCulture,
                $"  per-op: allocB={allocatedMb * 1048576 / operations:F0} cpuUs={cpuMs * 1000 / operations:F2}  ({unit})\n")
                .Append(CultureInfo.InvariantCulture,
                $"  client-per-op: allocB=0 cpuUs={clientCpu.TotalMilliseconds * 1000 / operations:F2}  ({unit}, native workers)\n");
        }

        internal static long NowNs()
        {
            // QueryPerformanceCounter, the CLOCK_MONOTONIC of the mingw-w64 workers
            return (long)((Int128)Stopwatch.GetTimestamp() * 1_000_000_000 / Stopwatch.Frequency);
        }

        private static Dictionary<string, string> WithIdentity(Dictionary<string, string> environment, string pki, string role)
        {
            var result = new Dictionary<string, string>(environment, StringComparer.Ordinal);
            if (environment.ContainsKey(WorkerSecurityPolicyVariable))
            {
                result[WorkerCertificateVariable] = Path.Combine(pki, role + "-cert.der");
                result[WorkerPrivateKeyVariable] = Path.Combine(pki, role + "-key.der");
            }
            return result;
        }

        /// <summary>
        /// A DER RSA-2048 key and self-signed SHA-256 certificate, as common/certificates.py writes them.
        /// </summary>
        private static void WriteIdentity(string directory, string role)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                WorkerSubjectPrefix + role,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddUri(new Uri(WorkerApplicationUriPrefix + role));
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.NonRepudiation,
                true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")],
                false));
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(30));
            File.WriteAllBytes(Path.Combine(directory, role + "-cert.der"), certificate.RawData);
            File.WriteAllBytes(Path.Combine(directory, role + "-key.der"), rsa.ExportRSAPrivateKey());
        }

        /// <summary>
        /// The server under test: the harness child or an external executable.
        /// </summary>
        private sealed class ServerHandle : IDisposable
        {
            public static async Task<ServerHandle> StartAsync(string scenario, int port, bool encrypt, string pki)
            {
                string? exe = Program.TryGetArg("server-exe", out string? value) ? value : null;
                var handle = new ServerHandle();
                if (exe == null)
                {
                    bool counters = scenario == "native-counters";
                    // As the benchmark runner sizes it for UA-.NETStandard 2.0: the whole client pipeline
                    // plus the three reserved bootstrap, reconnect and control slots.
                    int queue = Math.Max(200, (Program.ArgInt("clients", 10) * Program.ArgInt("inflight", 100)) + 3);
                    (handle.m_process, handle.m_lines) = Program.StartServerChild(
                        FormattableString.Invariant(
                        $"server --contract 1 --port {port} --threads {Program.Arg("threads", "200")} ") +
                        FormattableString.Invariant(
                        $"--queue {Program.Arg("queue", queue.ToString(CultureInfo.InvariantCulture))} ") +
                        FormattableString.Invariant(
                        $"--counters {(counters ? Program.Arg("nodes", "1") : "0")} --sampling {Program.Arg("sampling", "10")} ") +
                        FormattableString.Invariant(
                        $"--counter-lock {Program.Arg("counter-lock", "1")} --async-node-manager {Program.Arg("async-node-manager", "1")}"),
                        // the benchmark's .NET workers run with Server GC; SERVER_DOTNET_gcServer=0 overrides it
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["DOTNET_gcServer"] = "1" });
                    string ready = handle.m_lines.Take();
                    if (!ready.StartsWith("READY", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(ready);
                    }
                    Console.WriteLine(FormattableString.Invariant($"server pid={handle.m_process.Id} (harness, contract)"));
                }
                else
                {
                    string arguments = Program.Arg("server-args", "--port {port}")
                        .Replace("{port}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                        .Replace("{items}", Program.Arg("nodes", "1"), StringComparison.Ordinal)
                        .Replace("{sampling}", Program.Arg("sampling", "10"), StringComparison.Ordinal)
                        .Replace("{pki}", pki, StringComparison.Ordinal);
                    var environment = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (encrypt)
                    {
                        environment[WorkerSecurityPolicyVariable] = "Basic256Sha256";
                        environment[WorkerCertificateVariable] = Path.Combine(pki, "server-cert.der");
                        environment[WorkerPrivateKeyVariable] = Path.Combine(pki, "server-key.der");
                    }
                    handle.m_external = Worker.Start(exe, arguments, environment);
                    handle.m_process = handle.m_external.Process;
                    handle.m_external.WaitFor(l => l.Contains("ready", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(120));
                    Console.WriteLine(FormattableString.Invariant($"server pid={handle.m_process.Id} ({exe} {arguments})"));
                }
                if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
                    Program.TryGetArg("server-affinity", out string? affinity))
                {
                    // e.g. 0x1: one core, to compare the throughput per core with a single-threaded server
                    handle.m_process.ProcessorAffinity = (nint)Convert.ToInt64(affinity, 16);
                }
                if (Program.TryGetArg("server-priority", out string? priority))
                {
                    handle.m_process.PriorityClass = Enum.Parse<ProcessPriorityClass>(priority, true);
                }
                // let the listener settle before the workers connect
                await Task.Delay(500).ConfigureAwait(false);
                return handle;
            }

            /// <summary>
            /// Attaches dotnet-trace for <paramref name="durationSeconds"/> (default <c>--duration</c>)
            /// if <c>--trace</c> is given, and waits 2 s for the session to start.
            /// </summary>
            public Process? StartTracer(int? durationSeconds = null)
            {
                Process? tracer = Program.StartTracer(m_process!.Id, durationSeconds ?? Program.ArgInt("duration", 20), out _);
                if (tracer != null)
                {
                    Thread.Sleep(2000);
                }
                return tracer;
            }

            public void Arm(string arm)
            {
                // the open62541 counter server advances its counters only while armed
                if (m_external != null && Program.Arg("scenario", string.Empty) == "native-counters")
                {
                    m_external.Send(arm);
                }
            }

            public async Task MarkAsync()
            {
                if (Program.ArgInt("thread-states", 0) != 0)
                {
                    StartThreadSampler();
                }
                m_markTimestamp = Stopwatch.GetTimestamp();
                if (m_external == null)
                {
                    await m_process!.StandardInput.WriteLineAsync("mark").ConfigureAwait(false);
                    Program.Take(m_lines!, "MARKED");
                    m_markTimestamp = Stopwatch.GetTimestamp();
                }
                else
                {
                    m_process!.Refresh();
                    m_markCpu = m_process.TotalProcessorTime;
                }
            }

            public async Task<string> ReportAsync()
            {
                string sched = StopThreadSampler();
                if (m_external == null)
                {
                    await m_process!.StandardInput.WriteLineAsync("report").ConfigureAwait(false);
                    return Program.Take(m_lines!, "REPORT")[7..] + sched;
                }
                m_process!.Refresh();
                double seconds = Stopwatch.GetElapsedTime(m_markTimestamp).TotalSeconds;
                TimeSpan cpu = m_process.TotalProcessorTime - m_markCpu;
                return FormattableString.Invariant(
                    $"sec={seconds:F2} cpuMs={cpu.TotalMilliseconds:F0} cpuCores={cpu.TotalSeconds / seconds:F2} ") +
                    FormattableString.Invariant(
                    $"threads={m_process.Threads.Count} wsMB={m_process.WorkingSet64 / 1048576} (external)") +
                    sched;
            }

            /// <summary>
            /// Samples the scheduling state of the server threads every ~3 ms until the report:
            /// Running, and Ready or Standby (waiting for a processor, so the server's processors
            /// are oversubscribed). Runs in this process, not in the server.
            /// </summary>
            private void StartThreadSampler()
            {
                m_sampling = true;
                int pid = m_process!.Id;
                m_samplerThread = new Thread(() =>
                {
                    while (Volatile.Read(ref m_sampling))
                    {
                        int running = 0;
                        int ready = 0;
                        try
                        {
                            using var process = Process.GetProcessById(pid);
                            foreach (ProcessThread thread in process.Threads)
                            {
                                try
                                {
                                    System.Diagnostics.ThreadState state = thread.ThreadState;
                                    if (state == System.Diagnostics.ThreadState.Running)
                                    {
                                        running++;
                                    }
                                    else if (state is System.Diagnostics.ThreadState.Ready or
                                        System.Diagnostics.ThreadState.Standby)
                                    {
                                        ready++;
                                    }
                                }
                                catch (InvalidOperationException)
                                {
                                    // the thread exited
                                }
                                finally
                                {
                                    thread.Dispose();
                                }
                            }
                        }
                        catch (ArgumentException)
                        {
                            // the server exited
                            break;
                        }
                        m_samples++;
                        m_running += running;
                        m_ready += ready;
                        m_readyMax = Math.Max(m_readyMax, ready);
                        Thread.Sleep(3);
                    }
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                m_samplerThread.Start();
            }

            private string StopThreadSampler()
            {
                if (m_samplerThread == null)
                {
                    return string.Empty;
                }
                Volatile.Write(ref m_sampling, false);
                m_samplerThread.Join();
                return m_samples == 0 ? string.Empty : FormattableString.Invariant(
                    $" sched: samples={m_samples} running={(double)m_running / m_samples:F2} ") +
                    FormattableString.Invariant(
                    $"ready={(double)m_ready / m_samples:F2} readyMax={m_readyMax}");
            }

            public async Task StopAsync()
            {
                if (m_external == null)
                {
                    await m_process!.StandardInput.WriteLineAsync("quit").ConfigureAwait(false);
                    if (!m_process.WaitForExit(10000))
                    {
                        m_process.Kill();
                    }
                }
            }

            public void Dispose()
            {
                if (m_external != null)
                {
                    m_external.Dispose();
                }
                else
                {
                    if (!m_process!.HasExited)
                    {
                        m_process.Kill();
                    }
                    m_process.Dispose();
                    m_lines!.Dispose();
                }
            }

            private Process? m_process;
            private BlockingCollection<string>? m_lines;
            private Worker? m_external;
            private long m_markTimestamp;
            private TimeSpan m_markCpu;
            private bool m_sampling;
            private Thread? m_samplerThread;
            private long m_samples;
            private long m_running;
            private long m_ready;
            private int m_readyMax;
        }

        /// <summary>
        /// A native worker process with line-oriented stdin/stdout control.
        /// </summary>
        private sealed class Worker : IDisposable
        {
            private Worker(Process process)
            {
                Process = process;
            }

            public Process Process { get; }

            /// <summary>
            /// Processor time; still readable after the worker exited, as the handle is kept open.
            /// </summary>
            public TimeSpan Cpu
            {
                get
                {
                    Process.Refresh();
                    return Process.TotalProcessorTime;
                }
            }

            /// <summary>
            /// Starts a client worker, on the processors of <c>--client-affinity</c> if given.
            /// </summary>
            public static Worker StartClient(string exe, string arguments, Dictionary<string, string> environment)
            {
                Worker worker = Start(exe, arguments, environment);
                if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
                    Program.TryGetArg("client-affinity", out string? affinity))
                {
                    // e.g. the processors of --server-affinity, to co-locate clients and server
                    worker.Process.ProcessorAffinity = (nint)Convert.ToInt64(affinity, 16);
                }
                return worker;
            }

            public static Worker Start(string exe, string arguments, Dictionary<string, string> environment)
            {
                // resolve a relative path before the working directory changes to the worker's directory
                if (!string.IsNullOrEmpty(Path.GetDirectoryName(exe)))
                {
                    exe = Path.GetFullPath(exe);
                }
                var startInfo = new ProcessStartInfo(exe, arguments)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))
                };
                foreach (KeyValuePair<string, string> variable in environment)
                {
                    startInfo.Environment[variable.Key] = variable.Value;
                }
                var worker = new Worker(Process.Start(startInfo)!);
                worker.Process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        worker.m_lines.CompleteAdding();
                    }
                    else
                    {
                        worker.m_lines.Add(e.Data);
                    }
                };
                // the workers log to stderr; drain it so that they never block
                worker.Process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null && Program.Arg("verbose", "0") != "0")
                    {
                        Console.WriteLine("[worker] " + e.Data);
                    }
                };
                worker.Process.BeginOutputReadLine();
                worker.Process.BeginErrorReadLine();
                return worker;
            }

            public string WaitFor(Func<string, bool> predicate, TimeSpan timeout)
            {
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    TimeSpan left = timeout - deadline.Elapsed;
                    if (left <= TimeSpan.Zero || !m_lines.TryTake(out string? line, left))
                    {
                        throw new TimeoutException(FormattableString.Invariant(
                            $"{Path.GetFileName(Process.StartInfo.FileName)} (pid {Process.Id}) did not report in {timeout.TotalSeconds:F0} s"));
                    }
                    if (predicate(line.Trim()))
                    {
                        return line.Trim();
                    }
                    if (Program.Arg("verbose", "0") != "0")
                    {
                        Console.WriteLine("[worker] " + line);
                    }
                }
            }

            public void Send(string line)
            {
                Process.StandardInput.WriteLine(line);
                Process.StandardInput.Flush();
            }

            public void Dispose()
            {
                try
                {
                    if (!Process.HasExited)
                    {
                        Process.StandardInput.Close();
                        if (!Process.WaitForExit(5000))
                        {
                            Process.Kill();
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // already gone
                }
                Process.Dispose();
                m_lines.Dispose();
            }

            private readonly BlockingCollection<string> m_lines = [];
        }
    }

    /// <summary>
    /// The address space of the benchmark servers: 100 writable Int32 variables
    /// ns=1;i=1001..1100 in the namespace of the server URI.
    /// </summary>
    internal sealed class BenchmarkContractNodeManager : CustomNodeManager2
    {
        public BenchmarkContractNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : base(server, configuration, NativeWorkerBenchmark.ContractServerUri)
        {
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            for (int i = 0; i < NativeWorkerBenchmark.NodeCount; i++)
            {
                uint id = NativeWorkerBenchmark.FirstNodeId + (uint)i;
                string name = "Value" + id.ToString(CultureInfo.InvariantCulture);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(id, NamespaceIndex),
                    BrowseName = new QualifiedName(name, NamespaceIndex),
                    DisplayName = new LocalizedText(name),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Value = new Variant(i),
                    StatusCode = StatusCodes.Good,
                    Timestamp = DateTimeUtc.Now
                };
                node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                AddPredefinedNode(SystemContext, node);
            }
        }
    }

    /// <summary>
    /// The benchmark contract on <see cref="AsyncCustomNodeManager"/>, the node manager base
    /// without the node manager lock on the read path.
    /// </summary>
    internal sealed class AsyncBenchmarkContractNodeManager : AsyncCustomNodeManager
    {
        public AsyncBenchmarkContractNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            : base(server, configuration, NativeWorkerBenchmark.ContractServerUri)
        {
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            for (int i = 0; i < NativeWorkerBenchmark.NodeCount; i++)
            {
                uint id = NativeWorkerBenchmark.FirstNodeId + (uint)i;
                string name = "Value" + id.ToString(CultureInfo.InvariantCulture);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(id, NamespaceIndex),
                    BrowseName = new QualifiedName(name, NamespaceIndex),
                    DisplayName = new LocalizedText(name),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Value = new Variant(i),
                    StatusCode = StatusCodes.Good,
                    Timestamp = DateTimeUtc.Now
                };
                node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// A server exposing the benchmark contract in addition to the standard nodes.
    /// </summary>
    internal sealed class BenchmarkContractServer : StandardServer
    {
        public BenchmarkContractServer(ITelemetryContext telemetry, bool asyncNodeManager)
            : base(telemetry)
        {
            m_asyncNodeManager = asyncNodeManager;
        }

        protected override IMasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_asyncNodeManager
                ? new MasterNodeManager(server, configuration, null, new AsyncBenchmarkContractNodeManager(server, configuration))
                : new MasterNodeManager(server, configuration, null, new BenchmarkContractNodeManager(server, configuration));
        }

        private readonly bool m_asyncNodeManager;
    }
}
