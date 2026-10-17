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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using IServerInternal = Opc.Ua.Server.IServerInternal;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Server side of the <c>events</c> scenario: reports BaseEvents on the Server object,
    /// as the Sessions <c>ServerEventSubscribeLoadTest</c> does. Started and stopped through
    /// the <c>events</c> stdin command of the server child.
    /// </summary>
    internal sealed class EventGenerator : IDisposable
    {
        public EventGenerator(IServerInternal server)
        {
            m_server = server;
        }

        /// <summary>
        /// Starts reporting <paramref name="rate"/> events per second, or as fast as one
        /// thread can when the rate is 0.
        /// </summary>
        public void Start(int rate)
        {
            Stop();
            m_stop = new CancellationTokenSource();
            CancellationToken ct = m_stop.Token;
            m_thread = new Thread(() => Run(rate, ct)) { IsBackground = true, Name = "EventGenerator" };
            m_thread.Start();
        }

        public void Stop()
        {
            m_stop?.Cancel();
            m_thread?.Join();
            m_stop?.Dispose();
            m_stop = null;
            m_thread = null;
        }

        public void Mark()
        {
            Interlocked.Exchange(ref m_generated, 0);
        }

        public string Report()
        {
            return FormattableString.Invariant($"events={Interlocked.Read(ref m_generated)}");
        }

        public void Dispose()
        {
            Stop();
        }

        private void Run(int rate, CancellationToken ct)
        {
            ISystemContext context = m_server.DefaultSystemContext;
            var clock = Stopwatch.StartNew();
            long sent = 0;
            while (!ct.IsCancellationRequested)
            {
                if (rate > 0 && sent >= (long)(clock.Elapsed.TotalSeconds * rate))
                {
                    // paced: report what the elapsed time allows, then sleep
                    Thread.Sleep(1);
                    continue;
                }
                var e = new BaseEventState(null);
                e.Initialize(
                    context,
                    m_server.ServerObject,
                    EventSeverity.Medium,
                    new LocalizedText(string.Create(CultureInfo.InvariantCulture, $"Load event {sent}")));
                m_server.ReportEvent(context, e);
                sent++;
                Interlocked.Increment(ref m_generated);
            }
        }

        private readonly IServerInternal m_server;
        private CancellationTokenSource? m_stop;
        private Thread? m_thread;
        private long m_generated;
    }

    /// <summary>
    /// Client side of the <c>events</c> scenario: event subscriptions on the Server object
    /// with the select clauses of the Sessions <c>ServerEventSubscribeLoadTest</c>. Counts the
    /// delivered events and the delay from the event Time to the client callback.
    /// </summary>
    internal sealed class EventWorkload : Workload
    {
        public EventWorkload(ISession[] sessions, int subscriptions, int publishingInterval, int queueSize, int rate)
        {
            m_sessions = sessions;
            m_subscriptions = subscriptions;
            m_publishingInterval = publishingInterval;
            m_queueSize = queueSize;
            Rate = rate;
        }

        /// <summary>
        /// Server-side events per second (0 = unpaced).
        /// </summary>
        public int Rate { get; }

        public override double Operations => m_received;

        public override string OperationName => "event notification";

        public override (double Count, string Name)? Secondary =>
            m_generated > 0 ? (m_generated, "generated event") : null;

        public override async Task SetupAsync()
        {
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
                $"events: {m_sessions.Length} sessions x {m_subscriptions} event subs, pub {m_publishingInterval} ms, ") +
                FormattableString.Invariant(
                $"queue {m_queueSize}, rate {(Rate == 0 ? "unpaced" : Rate.ToString(CultureInfo.InvariantCulture))}; setup {setupTime.ElapsedMilliseconds} ms"));
        }

        public override Task RunAsync(CancellationToken ct)
        {
            // the server child generates the events
            return Task.Delay(Timeout.Infinite, ct);
        }

        public override void ResetStats()
        {
            m_latency.Clear();
            Interlocked.Exchange(ref m_events, 0);
        }

        public override void OnServerReport(string report)
        {
            // notifications keep arriving after the window: report the count at its end
            m_received = Interlocked.Read(ref m_events);
            foreach (string part in report.Split(' '))
            {
                if (part.StartsWith("events=", StringComparison.Ordinal))
                {
                    m_generated = long.Parse(part[7..], CultureInfo.InvariantCulture);
                }
            }
        }

        public override string Report(double seconds)
        {
            long events = m_received;
            long expected = m_generated * m_sessions.Length * m_subscriptions;
            double ratio = expected == 0 ? 0 : (double)events / expected;
            return FormattableString.Invariant(
                $"generated/s={m_generated / seconds:F0} events/s={events / seconds:F0} received/expected={ratio:P1} ") +
                FormattableString.Invariant($"event->notify latency {m_latency.Percentiles()}");
        }

        private async Task CreateSubscriptionAsync(ISession session, CancellationToken ct)
        {
#pragma warning disable CA2000 // ownership transfers to the session in AddSubscription; disposed below on failure before that
            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = m_publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                FastEventCallback = OnEvents
            };
#pragma warning restore CA2000
            try
            {
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventId));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.EventType));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.SourceNode));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.SourceName));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Time));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Severity));
                subscription.AddItem(new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    MonitoringMode = MonitoringMode.Reporting,
                    Filter = filter,
                    QueueSize = (uint)m_queueSize
                });
                session.AddSubscription(subscription);
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
            await subscription.CreateAsync(ct).ConfigureAwait(false);
        }

        private void OnEvents(Subscription subscription, EventNotificationList notification, ArrayOf<string> stringTable)
        {
            DateTime now = DateTime.UtcNow;
            int count = 0;
            foreach (EventFieldList fields in notification.Events)
            {
                count++;
                // sample every 16th event so the recorder stays small at high rates
                if ((count & 15) == 1 && fields.EventFields.Count > 4 &&
                    fields.EventFields[4].TryGetValue(out DateTimeUtc time))
                {
                    m_latency.Record((long)((now - ((DateTime)time).ToUniversalTime()).TotalSeconds * Stopwatch.Frequency));
                }
            }
            Interlocked.Add(ref m_events, count);
        }

        private readonly ISession[] m_sessions;
        private readonly int m_subscriptions;
        private readonly int m_publishingInterval;
        private readonly int m_queueSize;
        private readonly LatencyRecorder m_latency = new();
        private long m_events;
        private long m_generated;
        private long m_received;
    }
}
