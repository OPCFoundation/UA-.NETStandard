// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Adds, reloads and removes runtime NodeSet NodeManagers through the public
    /// <see cref="INodeManagerLifecycle"/> API of the server child while the client drives load
    /// (the <c>lifecycle-reload</c> scenario).
    /// </summary>
    /// <remarks>
    /// Manager <c>i</c> owns namespace <see cref="NamespaceUri"/>(i) with one Root object under
    /// the Objects folder, a custom ObjectType (so every publication copies the type tree) and
    /// <c>variables</c> writable Int32 variables <c>i=1000..</c>. Each worker owns a disjoint set
    /// of slots and, every interval, reloads one of them (Migrate, Invalidate or alternating),
    /// every <c>removeEvery</c>-th operation removes and re-adds it instead, and every
    /// <c>abortEvery</c>-th operation prepares a reload and disposes it uncommitted. The time from
    /// the commit to <see cref="LifecycleResult.Retired"/> is tracked without blocking the worker.
    /// </remarks>
    internal sealed class LifecycleChurn : IAsyncDisposable
    {
        public LifecycleChurn(StandardServer server, int managers, int variables)
        {
            m_server = server;
            m_slots = new NodeManagerSlot?[managers];
            m_variables = variables;
        }

        public static string NamespaceUri(int index)
        {
            return FormattableString.Invariant($"urn:perf:lifecycle:{index}");
        }

        /// <summary>
        /// Adds every manager, one publication each, before the server reports ready.
        /// </summary>
        public async Task InitializeAsync()
        {
            var time = Stopwatch.StartNew();
            for (int i = 0; i < m_slots.Length; i++)
            {
                LifecycleResult result = await Lifecycle.ApplyAsync([LifecycleChange.Add(CreateFactory(i))])
                    .ConfigureAwait(false);
                m_slots[i] = result.Registrations[0].Slot;
            }
            Console.Error.WriteLine(FormattableString.Invariant(
                $"lifecycle: {m_slots.Length} managers x {m_variables} variables added in {time.ElapsedMilliseconds} ms"));
        }

        /// <summary>
        /// Starts <paramref name="workers"/> churn loops.
        /// </summary>
        public void Start(int intervalMs, string mode, int removeEvery, int abortEvery, int workers)
        {
            if (m_stop != null || m_slots.Length == 0)
            {
                return;
            }
            m_stop = new CancellationTokenSource();
            workers = Math.Clamp(workers, 1, m_slots.Length);
            for (int w = 0; w < workers; w++)
            {
                int worker = w;
                m_workers.Add(Task.Run(() => RunWorkerAsync(worker, workers, intervalMs, mode, removeEvery, abortEvery, m_stop.Token)));
            }
        }

        public async Task StopAsync()
        {
            if (m_stop == null)
            {
                return;
            }
            await m_stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(m_workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // the workers stop by cancellation
            }
            m_workers.Clear();
            m_stop.Dispose();
            m_stop = null;
        }

        public void Mark()
        {
            m_prepare.Clear();
            m_commit.Clear();
            m_apply.Clear();
            m_retired.Clear();
            m_abort.Clear();
            Interlocked.Exchange(ref m_reloads, 0);
            Interlocked.Exchange(ref m_migrates, 0);
            Interlocked.Exchange(ref m_invalidates, 0);
            Interlocked.Exchange(ref m_removes, 0);
            Interlocked.Exchange(ref m_adds, 0);
            Interlocked.Exchange(ref m_aborts, 0);
            Interlocked.Exchange(ref m_failures, 0);
            Interlocked.Exchange(ref m_issues, 0);
            Interlocked.Exchange(ref m_maxPending, Volatile.Read(ref m_pending));
            m_failureTypes.Clear();
        }

        /// <summary>
        /// The counts and latencies of the window as space-separated key=value pairs;
        /// latencies as <c>name=p50/p99/max</c> in milliseconds.
        /// </summary>
        public string Report()
        {
            if (m_slots.Length == 0)
            {
                return string.Empty;
            }
            long reloads = Interlocked.Read(ref m_reloads);
            long removes = Interlocked.Read(ref m_removes);
            long adds = Interlocked.Read(ref m_adds);
            var report = new StringBuilder();
            report.Append(CultureInfo.InvariantCulture,
                $"lcOps={reloads + removes + adds} lcReloads={reloads} lcMigrate={Interlocked.Read(ref m_migrates)} ")
                .Append(CultureInfo.InvariantCulture,
                $"lcInvalidate={Interlocked.Read(ref m_invalidates)} lcRemoves={removes} lcAdds={adds} ")
                .Append(CultureInfo.InvariantCulture,
                $"lcAborts={Interlocked.Read(ref m_aborts)} lcFailures={Interlocked.Read(ref m_failures)} ")
                .Append(CultureInfo.InvariantCulture,
                $"lcIssues={Interlocked.Read(ref m_issues)} lcPending={Volatile.Read(ref m_pending)} lcMaxPending={Volatile.Read(ref m_maxPending)} ")
                .Append("lcPrepareMs=").Append(Compact(m_prepare))
                .Append(" lcCommitMs=").Append(Compact(m_commit))
                .Append(" lcApplyMs=").Append(Compact(m_apply))
                .Append(" lcRetiredMs=").Append(Compact(m_retired))
                .Append(" lcAbortMs=").Append(Compact(m_abort));
            if (!m_failureTypes.IsEmpty)
            {
                report.Append(" lcFailureTypes=").Append(string.Join(',', m_failureTypes.Select(p => p.Key + ":" + p.Value)));
            }
            return report.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        private INodeManagerLifecycle Lifecycle => m_server.NodeManagerLifecycle;

        private async Task RunWorkerAsync(
            int worker,
            int workers,
            int intervalMs,
            string mode,
            int removeEvery,
            int abortEvery,
            CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(1, intervalMs)));
            long operation = 0;
            int next = worker;
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    int index = next;
                    next += workers;
                    if (next >= m_slots.Length)
                    {
                        next = worker;
                    }
                    operation++;
                    try
                    {
                        if (m_slots[index] == null)
                        {
                            // an earlier re-add failed: retry it before churning this index again
                            await AddAsync(index).ConfigureAwait(false);
                        }
                        else if (abortEvery > 0 && operation % abortEvery == 0)
                        {
                            await AbortAsync(index, ct).ConfigureAwait(false);
                        }
                        else if (removeEvery > 0 && operation % removeEvery == 0)
                        {
                            await RemoveAndAddAsync(index, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            NodeManagerMonitoredItemTransitionMode transition = mode switch
                            {
                                "migrate" => NodeManagerMonitoredItemTransitionMode.Migrate,
                                "invalidate" => NodeManagerMonitoredItemTransitionMode.Invalidate,
                                _ => operation % 2 == 0
                                    ? NodeManagerMonitoredItemTransitionMode.Migrate
                                    : NodeManagerMonitoredItemTransitionMode.Invalidate
                            };
                            await ReloadAsync(index, transition, ct).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref m_failures);
                        m_failureTypes.AddOrUpdate(ex.GetType().Name, 1, (_, count) => count + 1);
                        if (Interlocked.Increment(ref m_printedFailures) <= 5)
                        {
                            Console.Error.WriteLine("lifecycle failure: " + ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
        }

        private async Task ReloadAsync(int index, NodeManagerMonitoredItemTransitionMode transition, CancellationToken ct)
        {
            NodeManagerSlot slot = m_slots[index] ?? throw new InvalidOperationException("Slot not added.");
            await ApplyAsync([LifecycleChange.Reload(slot, CreateFactory(index), transition)], ct).ConfigureAwait(false);
            Interlocked.Increment(ref m_reloads);
            Interlocked.Increment(ref transition == NodeManagerMonitoredItemTransitionMode.Migrate ? ref m_migrates : ref m_invalidates);
        }

        private async Task RemoveAndAddAsync(int index, CancellationToken ct)
        {
            NodeManagerSlot slot = m_slots[index] ?? throw new InvalidOperationException("Slot not added.");
            // the slot stays live until the removal commits; a failed removal leaves it in place
            await ApplyAsync([LifecycleChange.Remove(slot)], ct).ConfigureAwait(false);
            m_slots[index] = null;
            Interlocked.Increment(ref m_removes);
            await AddAsync(index).ConfigureAwait(false);
        }

        private async Task AddAsync(int index)
        {
            // the namespace stays registered; the next generation reuses its index. A failed add
            // leaves the slot null, and the worker retries the add on its next turn at this index.
            LifecycleResult added = await ApplyAsync([LifecycleChange.Add(CreateFactory(index))], CancellationToken.None)
                .ConfigureAwait(false);
            m_slots[index] = added.Registrations[0].Slot;
            Interlocked.Increment(ref m_adds);
        }

        private async Task AbortAsync(int index, CancellationToken ct)
        {
            NodeManagerSlot slot = m_slots[index] ?? throw new InvalidOperationException("Slot not added.");
            long start = Stopwatch.GetTimestamp();
            IPreparedChanges prepared = await Lifecycle
                .PrepareAsync([LifecycleChange.Reload(slot, CreateFactory(index))], ct)
                .ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            m_abort.Record(Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref m_aborts);
        }

        private async Task<LifecycleResult> ApplyAsync(ArrayOf<LifecycleChange> changes, CancellationToken ct)
        {
            long start = Stopwatch.GetTimestamp();
            IPreparedChanges prepared = await Lifecycle.PrepareAsync(changes, ct).ConfigureAwait(false);
            LifecycleResult result;
            await using (prepared.ConfigureAwait(false))
            {
                long prepareEnd = Stopwatch.GetTimestamp();
                m_prepare.Record(prepareEnd - start);
                // a removal must not be abandoned between its publication and the re-add
                result = await prepared.CommitAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
                long commitEnd = Stopwatch.GetTimestamp();
                m_commit.Record(commitEnd - prepareEnd);
                m_apply.Record(commitEnd - start);
                _ = TrackRetirementAsync(result, commitEnd);
            }
            if (result.Issues.Count > 0)
            {
                Interlocked.Increment(ref m_issues);
            }
            return result;
        }

        private async Task TrackRetirementAsync(LifecycleResult result, long committed)
        {
            long pending = Interlocked.Increment(ref m_pending);
            long max = Volatile.Read(ref m_maxPending);
            while (pending > max)
            {
                long seen = Interlocked.CompareExchange(ref m_maxPending, pending, max);
                if (seen == max)
                {
                    break;
                }
                max = seen;
            }
            try
            {
                await result.Retired.ConfigureAwait(false);
                m_retired.Record(Stopwatch.GetTimestamp() - committed);
            }
            catch (Exception ex)
            {
                m_failureTypes.AddOrUpdate("Retired" + ex.GetType().Name, 1, (_, count) => count + 1);
            }
            finally
            {
                Interlocked.Decrement(ref m_pending);
            }
        }

        private RuntimeNodeSetNodeManagerFactory CreateFactory(int index)
        {
            string namespaceUri = NamespaceUri(index);
            var xml = new StringBuilder(256 + (m_variables * 420));
            xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
                .Append("<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">")
                .Append("<NamespaceUris><Uri>").Append(namespaceUri).Append("</Uri></NamespaceUris>")
                .Append("<Models><Model ModelUri=\"").Append(namespaceUri).Append("\" /></Models>")
                .Append("<UAObjectType NodeId=\"ns=1;i=2\" BrowseName=\"1:PerfType\"><DisplayName>PerfType</DisplayName>")
                .Append("<References><Reference ReferenceType=\"i=45\" IsForward=\"false\">i=58</Reference></References>")
                .Append("</UAObjectType>")
                .Append("<UAObject NodeId=\"ns=1;i=1\" BrowseName=\"1:Root\"><DisplayName>Root</DisplayName>")
                .Append("<References><Reference ReferenceType=\"i=35\" IsForward=\"false\">i=85</Reference>")
                .Append("<Reference ReferenceType=\"i=40\">ns=1;i=2</Reference></References></UAObject>");
            for (int v = 0; v < m_variables; v++)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<UAVariable NodeId=\"ns=1;i={1000 + v}\" BrowseName=\"1:V{v}\" ")
                    .Append("DataType=\"i=6\" AccessLevel=\"3\" UserAccessLevel=\"3\">")
                    .Append(CultureInfo.InvariantCulture, $"<DisplayName>V{v}</DisplayName><References>")
                    .Append("<Reference ReferenceType=\"i=47\" IsForward=\"false\">ns=1;i=1</Reference>")
                    .Append("<Reference ReferenceType=\"i=40\">i=63</Reference></References>")
                    .Append(CultureInfo.InvariantCulture,
                        $"<Value><Int32 xmlns=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">{v}</Int32></Value>")
                    .Append("</UAVariable>");
            }
            xml.Append("</UANodeSet>");
            byte[] bytes = Encoding.UTF8.GetBytes(xml.ToString());
            return new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "PerfLifecycle" + index.ToString(CultureInfo.InvariantCulture),
                        _ => new ValueTask<Stream>(new MemoryStream(bytes, writable: false)),
                        [namespaceUri])
                ]
            });
        }

        private static string Compact(LatencyRecorder recorder)
        {
            return recorder.Compact();
        }

        private readonly StandardServer m_server;
        private readonly NodeManagerSlot?[] m_slots;
        private readonly int m_variables;
        private readonly List<Task> m_workers = [];
        private readonly LatencyRecorder m_prepare = new();
        private readonly LatencyRecorder m_commit = new();
        private readonly LatencyRecorder m_apply = new();
        private readonly LatencyRecorder m_retired = new();
        private readonly LatencyRecorder m_abort = new();
        private readonly ConcurrentDictionary<string, long> m_failureTypes = new(StringComparer.Ordinal);
        private CancellationTokenSource? m_stop;
        private long m_reloads;
        private long m_migrates;
        private long m_invalidates;
        private long m_removes;
        private long m_adds;
        private long m_aborts;
        private long m_failures;
        private long m_issues;
        private long m_pending;
        private long m_maxPending;
        private int m_printedFailures;
    }
}
