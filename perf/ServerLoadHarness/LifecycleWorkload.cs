// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using V2 = Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// The client side of the <c>lifecycle-reload</c> scenario: the write-driven subscription load
    /// of <c>sub</c> (optional), closed-loop Reads that alternate over the churned managers'
    /// namespaces, a sampled subscription on the churned managers' variables, idle sessions, and
    /// an activation probe. The server child adds, reloads and removes the managers
    /// (see <see cref="LifecycleChurn"/>) after setup.
    /// </summary>
    internal sealed class LifecycleWorkload : Workload
    {
        public LifecycleWorkload(
            SubscribeWorkload? load,
            ISession[] readers,
            ISession? monitor,
            int managers,
            int variables,
            int readNodes,
            int samplingInterval,
            int probeInterval,
            Func<CancellationToken, Task<ISession>>? connectProbe,
            string churnCommand)
        {
            m_load = load;
            m_readers = readers;
            m_monitor = monitor;
            m_managers = managers;
            m_variables = variables;
            m_readNodes = readNodes;
            m_samplingInterval = samplingInterval;
            m_probeInterval = probeInterval;
            m_connectProbe = connectProbe;
            ChurnCommand = churnCommand;
        }

        /// <summary>
        /// The <c>lifecycle start ...</c> command the driver sends to the server child.
        /// </summary>
        public string ChurnCommand { get; }

        /// <summary>
        /// Notifications of the subscription load, or the lifecycle operations of the window
        /// when the scenario runs without load (the cost per publication).
        /// </summary>
        public override double Operations => m_load != null
            ? m_load.Operations
            : m_lifecycleOperations;

        public override string OperationName => m_load != null ? "notification" : "lifecycle op";

        public override (double Count, string Name)? Secondary => m_load != null
            ? (m_lifecycleOperations, "lifecycle op")
            : m_finalReads > 0 ? (m_finalReads, "read request") : null;

        public override async Task SetupAsync()
        {
            if (m_load != null)
            {
                await m_load.SetupAsync().ConfigureAwait(false);
            }
            ISession? first = m_readers.FirstOrDefault() ?? m_monitor;
            if (first == null)
            {
                return;
            }
            var namespaces = new ushort[m_managers];
            for (int i = 0; i < m_managers; i++)
            {
                int index = first.NamespaceUris.GetIndex(LifecycleChurn.NamespaceUri(i));
                if (index < 0)
                {
                    throw new InvalidOperationException("The server has no namespace " + LifecycleChurn.NamespaceUri(i));
                }
                namespaces[i] = (ushort)index;
            }
            // node k reads manager k % N (N = --read-managers, default all): one Read touches min(K, N) managers
            int spread = Math.Clamp(Program.ArgInt("read-managers", m_managers), 1, m_managers);
            m_readIds = new NodeId[m_readNodes];
            for (int k = 0; k < m_readNodes; k++)
            {
                m_readIds[k] = new NodeId((uint)(1000 + (k / spread % m_variables)), namespaces[k % spread]);
            }
            if (m_monitor is ManagedSession managed)
            {
                var setup = new SubscriptionSetup();
                V2.ISubscription subscription = managed.AddSubscription(
                    new MonitorHandler(this, setup),
                    new V2.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(Math.Max(100, m_samplingInterval)),
                        PublishingEnabled = true,
                        KeepAliveCount = 10,
                        LifetimeCount = 100
                    });
                int items = 0;
                for (int m = 0; m < m_managers; m++)
                {
                    for (int v = 0; v < m_variables; v++)
                    {
                        if (!subscription.TryAddMonitoredItem(
                            FormattableString.Invariant($"{m}/{v}"),
                            new V2.MonitoredItems.MonitoredItemOptions
                            {
                                StartNodeId = new NodeId((uint)(1000 + v), namespaces[m]),
                                AttributeId = Attributes.Value,
                                MonitoringMode = MonitoringMode.Reporting,
                                SamplingInterval = TimeSpan.FromMilliseconds(m_samplingInterval),
                                QueueSize = 1
                            },
                            out _))
                        {
                            throw new InvalidOperationException("Could not add a lifecycle monitored item.");
                        }
                        items++;
                    }
                }
                await setup.WaitForReadyAsync(subscription, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(FormattableString.Invariant(
                    $"lifecycle: monitoring {items} variables of {m_managers} managers, sampling {m_samplingInterval} ms"));
            }
            Console.WriteLine(FormattableString.Invariant(
                $"lifecycle: {m_readers.Length} readers x {m_readNodes} nodes over ") +
                FormattableString.Invariant(
                $"{Math.Min(m_readNodes, Program.ArgInt("read-managers", m_managers))} managers, ") +
                FormattableString.Invariant(
                $"probe {(m_connectProbe == null ? "off" : $"every {m_probeInterval} ms")}; churn '{ChurnCommand}'"));
        }

        public override async Task RunAsync(CancellationToken ct)
        {
            var tasks = new List<Task>();
            if (m_load != null)
            {
                tasks.Add(m_load.RunAsync(ct));
            }
            foreach (ISession reader in m_readers)
            {
                tasks.Add(Task.Run(() => ReadLoopAsync(reader, ct), CancellationToken.None));
            }
            if (m_connectProbe != null)
            {
                tasks.Add(Task.Run(() => ProbeLoopAsync(ct), CancellationToken.None));
            }
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
        }

        public override void ResetStats()
        {
            m_load?.ResetStats();
            m_readLatency.Clear();
            m_connect.Clear();
            m_reactivate.Clear();
            m_close.Clear();
            Interlocked.Exchange(ref m_reads, 0);
            Interlocked.Exchange(ref m_badValues, 0);
            Interlocked.Exchange(ref m_readErrors, 0);
            Interlocked.Exchange(ref m_monitorGood, 0);
            Interlocked.Exchange(ref m_monitorBad, 0);
            Interlocked.Exchange(ref m_probeErrors, 0);
        }

        public override void OnServerReport(string report)
        {
            m_load?.OnServerReport(report);
            m_finalReads = Interlocked.Read(ref m_reads);
            foreach (string pair in report.Split(' '))
            {
                if (pair.StartsWith("lcOps=", StringComparison.Ordinal))
                {
                    m_lifecycleOperations = double.Parse(pair[6..], CultureInfo.InvariantCulture);
                }
            }
        }

        public override string Report(double seconds)
        {
            string load = m_load == null ? "load=off" : m_load.Report(seconds);
            return load +
                FormattableString.Invariant(
                $" reads/s={m_finalReads / seconds:F0} readBadValues={Interlocked.Read(ref m_badValues)} ") +
                FormattableString.Invariant(
                $"readErrors={Interlocked.Read(ref m_readErrors)} read latency {m_readLatency.Percentiles()} ") +
                FormattableString.Invariant(
                $"lcMonitorGood={Interlocked.Read(ref m_monitorGood)} lcMonitorBad={Interlocked.Read(ref m_monitorBad)} ") +
                FormattableString.Invariant(
                $"probeConnectMs={m_connect.Compact()} probeReactivateMs={m_reactivate.Compact()} ") +
                FormattableString.Invariant(
                $"probeCloseMs={m_close.Compact()} probeErrors={Interlocked.Read(ref m_probeErrors)}");
        }

        private async Task ReadLoopAsync(ISession reader, CancellationToken ct)
        {
            var nodes = new ReadValueId[m_readIds.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new ReadValueId { NodeId = m_readIds[i], AttributeId = Attributes.Value };
            }
            while (!ct.IsCancellationRequested)
            {
                long start = Stopwatch.GetTimestamp();
                ReadResponse response;
                try
                {
                    response = await reader.ReadAsync(null, 0, TimestampsToReturn.Neither, nodes, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ServiceResultException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (ServiceResultException ex)
                {
                    if (Interlocked.Increment(ref m_readErrors) < 5)
                    {
                        Console.WriteLine("lifecycle read error: " + ex.Message);
                    }
                    continue;
                }
                m_readLatency.Record(Stopwatch.GetTimestamp() - start);
                Interlocked.Increment(ref m_reads);
                int bad = 0;
                foreach (DataValue value in response.Results)
                {
                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        bad++;
                    }
                }
                if (bad > 0)
                {
                    Interlocked.Add(ref m_badValues, bad);
                }
            }
        }

        /// <summary>
        /// Opens a session (CreateSession + ActivateSession), re-activates it and closes it, once
        /// per interval: the activation cost against the open sessions and live generations.
        /// </summary>
        private async Task ProbeLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(m_probeInterval));
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    long start = Stopwatch.GetTimestamp();
                    ISession? session = null;
                    try
                    {
                        session = await m_connectProbe!(ct).ConfigureAwait(false);
                        long connected = Stopwatch.GetTimestamp();
                        m_connect.Record(connected - start);
                        await session.UpdateSessionAsync(new UserIdentity(), default, ct).ConfigureAwait(false);
                        long reactivated = Stopwatch.GetTimestamp();
                        m_reactivate.Record(reactivated - connected);
                        await session.CloseAsync(ct).ConfigureAwait(false);
                        m_close.Record(Stopwatch.GetTimestamp() - reactivated);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is ServiceResultException or InvalidOperationException)
                    {
                        if (Interlocked.Increment(ref m_probeErrors) < 5)
                        {
                            Console.WriteLine("lifecycle probe error: " + ex.Message);
                        }
                    }
                    finally
                    {
                        session?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
        }

        private sealed class MonitorHandler : V2.ISubscriptionNotificationHandler
        {
            public MonitorHandler(LifecycleWorkload owner, SubscriptionSetup setup)
            {
                m_owner = owner;
                m_setup = setup;
            }

            public ValueTask OnDataChangeNotificationAsync(
                V2.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<V2.DataValueChange> notification,
                V2.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                int bad = 0;
                foreach (V2.DataValueChange change in notification.Span)
                {
                    if (StatusCode.IsBad(change.Value.StatusCode))
                    {
                        bad++;
                    }
                }
                Interlocked.Add(ref m_owner.m_monitorBad, bad);
                Interlocked.Add(ref m_owner.m_monitorGood, notification.Length - bad);
                return ValueTask.CompletedTask;
            }

            public ValueTask OnEventDataNotificationAsync(
                V2.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<V2.EventNotification> notification,
                V2.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                V2.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                V2.PublishState publishStateMask)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                V2.ISubscription subscription,
                V2.SubscriptionState state,
                V2.PublishState publishStateMask,
                CancellationToken ct = default)
            {
                m_setup.OnStateChanged(state);
                return ValueTask.CompletedTask;
            }

            private readonly LifecycleWorkload m_owner;
            private readonly SubscriptionSetup m_setup;
        }

        private readonly SubscribeWorkload? m_load;
        private readonly ISession[] m_readers;
        private readonly ISession? m_monitor;
        private readonly int m_managers;
        private readonly int m_variables;
        private readonly int m_readNodes;
        private readonly int m_samplingInterval;
        private readonly int m_probeInterval;
        private readonly Func<CancellationToken, Task<ISession>>? m_connectProbe;
        private readonly LatencyRecorder m_readLatency = new();
        private readonly LatencyRecorder m_connect = new();
        private readonly LatencyRecorder m_reactivate = new();
        private readonly LatencyRecorder m_close = new();
        private NodeId[] m_readIds = [];
        private long m_reads;
        private long m_badValues;
        private long m_readErrors;
        private long m_monitorGood;
        private long m_monitorBad;
        private long m_probeErrors;
        private double m_lifecycleOperations;
        private long m_finalReads;
    }
}
