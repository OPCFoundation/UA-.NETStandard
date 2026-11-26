// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// The <c>historyread-paging</c> scenario: setup inserts <c>--samples</c> raw values into
    /// each of <c>--hist-nodes</c> historizing variables of the ReferenceServer (its in-memory
    /// historian), then every session scans one node at a time with ReadRawModifiedDetails of
    /// <c>--page</c> values per request, following continuation points to the end. An operation
    /// is one page (one HistoryRead request).
    /// </summary>
    internal sealed class HistoryPagingWorkload : Workload
    {
        public HistoryPagingWorkload(ISession[] sessions, int nodes, int samples, int page)
        {
            m_sessions = sessions;
            m_nodeCount = Math.Clamp(nodes, 1, s_nodes.Length);
            m_samples = samples;
            m_page = page;
        }

        public override double Operations => m_finalPages;

        public override string OperationName => "history page";

        public override (double Count, string Name)? Secondary => (m_finalValues, "history value");

        public override async Task SetupAsync()
        {
            var time = Stopwatch.StartNew();
            ISession session = m_sessions[0];
            ushort ns = (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/Quickstarts/ReferenceServer");
            m_nodeIds = [.. s_nodes.Take(m_nodeCount).Select(n => new NodeId(n.Name, ns))];
            for (int n = 0; n < m_nodeIds.Length; n++)
            {
                for (int first = 0; first < m_samples; first += 1000)
                {
                    int count = Math.Min(1000, m_samples - first);
                    var values = new DataValue[count];
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = new DataValue(
                            s_nodes[n].Value(first + i),
                            StatusCodes.Good,
                            s_start.AddSeconds(first + i));
                    }
                    var update = new UpdateDataDetails
                    {
                        NodeId = m_nodeIds[n],
                        PerformInsertReplace = PerformUpdateType.Insert,
                        UpdateValues = values.ToArrayOf()
                    };
                    HistoryUpdateResponse response = await session.HistoryUpdateAsync(
                        null, [new ExtensionObject(update)], CancellationToken.None).ConfigureAwait(false);
                    if (StatusCode.IsBad(response.Results[0].StatusCode) ||
                        response.Results[0].OperationResults.ToArray()!.Any(StatusCode.IsBad))
                    {
                        throw new InvalidOperationException(
                            "HistoryUpdate insert failed on " + m_nodeIds[n] + ": " + response.Results[0].StatusCode);
                    }
                }
            }
            Console.WriteLine(FormattableString.Invariant(
                $"historyread-paging: {m_nodeIds.Length} nodes x {m_samples} samples inserted in {time.ElapsedMilliseconds} ms, ") +
                FormattableString.Invariant(
                $"page {m_page}, {m_sessions.Length} sessions"));
        }

        public override async Task RunAsync(CancellationToken ct)
        {
            await Task.WhenAll(m_sessions.Select((s, i) => Task.Run(() => ScanLoopAsync(s, i, ct), CancellationToken.None)))
                .ConfigureAwait(false);
        }

        public override void ResetStats()
        {
            m_pageLatency.Clear();
            m_scanLatency.Clear();
            Interlocked.Exchange(ref m_pages, 0);
            Interlocked.Exchange(ref m_values, 0);
            Interlocked.Exchange(ref m_scans, 0);
            Interlocked.Exchange(ref m_errors, 0);
            Interlocked.Exchange(ref m_shortScans, 0);
        }

        public override void OnServerReport(string report)
        {
            m_finalPages = Interlocked.Read(ref m_pages);
            m_finalValues = Interlocked.Read(ref m_values);
        }

        public override string Report(double seconds)
        {
            return FormattableString.Invariant(
                $"pages/s={m_finalPages / seconds:F0} values/s={m_finalValues / seconds:F0} ") +
                FormattableString.Invariant(
                $"scans={Interlocked.Read(ref m_scans)} shortScans={Interlocked.Read(ref m_shortScans)} ") +
                FormattableString.Invariant(
                $"errors={Interlocked.Read(ref m_errors)} page latency {m_pageLatency.Percentiles()} ") +
                FormattableString.Invariant($"scan latency {m_scanLatency.Percentiles()}");
        }

        private async Task ScanLoopAsync(ISession session, int index, CancellationToken ct)
        {
            var details = new ExtensionObject(new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = s_start,
                EndTime = s_start.AddSeconds(m_samples),
                NumValuesPerNode = (uint)m_page,
                ReturnBounds = false
            });
            int node = index % m_nodeIds.Length;
            while (!ct.IsCancellationRequested)
            {
                long scanStart = Stopwatch.GetTimestamp();
                ByteString continuation = default;
                long values = 0;
                try
                {
                    do
                    {
                        long start = Stopwatch.GetTimestamp();
                        HistoryReadResponse response = await session.HistoryReadAsync(
                            null,
                            details,
                            TimestampsToReturn.Source,
                            false,
                            [new HistoryReadValueId { NodeId = m_nodeIds[node], ContinuationPoint = continuation }],
                            ct).ConfigureAwait(false);
                        m_pageLatency.Record(Stopwatch.GetTimestamp() - start);
                        HistoryReadResult result = response.Results[0];
                        if (StatusCode.IsBad(result.StatusCode))
                        {
                            throw new ServiceResultException(
                                result.StatusCode,
                                FormattableString.Invariant(
                                    $"node {m_nodeIds[node]} after {values} values ({(continuation.IsEmpty ? "first" : "next")} page)"));
                        }
                        int count = result.HistoryData.TryGetValue(out HistoryData? data) && data != null
                            ? data.DataValues.Count
                            : 0;
                        values += count;
                        Interlocked.Increment(ref m_pages);
                        Interlocked.Add(ref m_values, count);
                        continuation = result.ContinuationPoint;
                    }
                    while (!continuation.IsEmpty);
                }
                catch (OperationCanceledException)
                {
                    await ReleaseAsync(session, details, node, continuation).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException) when (ct.IsCancellationRequested)
                {
                    await ReleaseAsync(session, details, node, continuation).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException ex)
                {
                    if (Interlocked.Increment(ref m_errors) < 5)
                    {
                        Console.WriteLine(FormattableString.Invariant(
                            $"history read error: {ex.StatusCode} {ex.Message}"));
                    }
                    // an abandoned scan must not hold a continuation point for the rest of the run
                    await ReleaseAsync(session, details, node, continuation).ConfigureAwait(false);
                    continue;
                }
                m_scanLatency.Record(Stopwatch.GetTimestamp() - scanStart);
                Interlocked.Increment(ref m_scans);
                if (values < m_samples)
                {
                    // the seeded history of the node adds values; fewer than inserted is a loss
                    Interlocked.Increment(ref m_shortScans);
                }
                node = (node + 1) % m_nodeIds.Length;
            }
        }

        /// <summary>
        /// Releases the continuation point of an abandoned scan (best effort, bounded by 5 s).
        /// </summary>
        private async Task ReleaseAsync(ISession session, ExtensionObject details, int node, ByteString continuation)
        {
            if (continuation.IsEmpty)
            {
                return;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await session.HistoryReadAsync(
                    null,
                    details,
                    TimestampsToReturn.Source,
                    true,
                    [new HistoryReadValueId { NodeId = m_nodeIds[node], ContinuationPoint = continuation }],
                    timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ServiceResultException or OperationCanceledException)
            {
                // the session is closing or the point is already gone
            }
        }

        private static readonly DateTime s_start = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly (string Name, Func<int, Variant> Value)[] s_nodes =
        [
            ("Scalar_Static_Double", i => new Variant((double)i)),
            ("Scalar_Static_Int32", i => new Variant(i)),
            ("Scalar_Static_Int64", i => new Variant((long)i)),
            ("Scalar_Static_UInt32", i => new Variant((uint)i)),
            ("Scalar_Static_Float", i => new Variant((float)i)),
            ("Scalar_Static_UInt64", i => new Variant((ulong)i))
        ];

        private readonly ISession[] m_sessions;
        private readonly int m_nodeCount;
        private readonly int m_samples;
        private readonly int m_page;
        private readonly LatencyRecorder m_pageLatency = new();
        private readonly LatencyRecorder m_scanLatency = new();
        private NodeId[] m_nodeIds = [];
        private long m_pages;
        private long m_values;
        private long m_scans;
        private long m_errors;
        private long m_shortScans;
        private long m_finalPages;
        private long m_finalValues;
    }
}
