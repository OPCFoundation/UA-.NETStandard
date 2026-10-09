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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Client observer of the counter workload: one subscription with a monitored item per
    /// counter (queue size one, discard oldest). A counter advances once per server sample,
    /// so its progress over the window, against window / sampling interval, is the share of
    /// samples the server took. Every item must reach 99% to pass, as in the o6-automation
    /// opcua-benchmarks subscription suite.
    /// </summary>
    internal sealed class CounterWorkload : Workload
    {
        public CounterWorkload(ISession session, int count, int samplingInterval, int publishingInterval)
        {
            m_session = session;
            m_samplingInterval = samplingInterval;
            m_publishingInterval = publishingInterval;
            m_latestValue = new ulong[count];
            m_latestTicks = new long[count];
            m_startValue = new ulong[count];
            m_startTicks = new long[count];
        }

        public override double Operations => Interlocked.Read(ref m_notifications);

        public override string OperationName => "notification";

        public override async Task SetupAsync()
        {
            ushort namespaceIndex = (ushort)m_session.NamespaceUris.GetIndex(CounterNodeManager.Namespace);
#pragma warning disable CA2000 // ownership transfers to the session in AddSubscription; disposed below on failure before that
            var subscription = new Subscription(m_session.DefaultSubscription)
            {
                PublishingInterval = m_publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 10000,
                MaxNotificationsPerPublish = 0,
                TimestampsToReturn = TimestampsToReturn.Neither,
                FastDataChangeCallback = OnDataChange
            };
#pragma warning restore CA2000
            try
            {
                for (int i = 0; i < m_latestValue.Length; i++)
                {
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = new NodeId((uint)i + 1, namespaceIndex),
                        AttributeId = Attributes.Value,
                        MonitoringMode = MonitoringMode.Reporting,
                        SamplingInterval = m_samplingInterval,
                        QueueSize = 1,
                        DiscardOldest = true
                    };
                    m_indexByHandle[item.ClientHandle] = i;
                    subscription.AddItem(item);
                }
                // the session owns and disposes the subscription from here on
                m_session.AddSubscription(subscription);
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
            var setup = Stopwatch.StartNew();
            await subscription.CreateAsync().ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant(
                $"counters: {m_latestValue.Length} items, sampling {m_samplingInterval} ms, ") +
                FormattableString.Invariant(
                $"publishing {m_publishingInterval} ms, revised sampling {System.Linq.Enumerable.FirstOrDefault(subscription.MonitoredItems)?.Status.SamplingInterval}; setup {setup.ElapsedMilliseconds} ms"));
        }

        public override async Task RunAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }

        public override void ResetStats()
        {
            lock (m_lock)
            {
                Array.Copy(m_latestValue, m_startValue, m_latestValue.Length);
                Array.Copy(m_latestTicks, m_startTicks, m_latestTicks.Length);
            }
            Interlocked.Exchange(ref m_notifications, 0);
        }

        public override string Report(double seconds)
        {
            double min = double.MaxValue;
            double sum = 0;
            int below = 0;
            int missing = 0;
            lock (m_lock)
            {
                for (int i = 0; i < m_latestValue.Length; i++)
                {
                    if (m_startTicks[i] == 0 || m_latestTicks[i] <= m_startTicks[i])
                    {
                        missing++;
                        min = 0;
                        continue;
                    }
                    double elapsedMs = (m_latestTicks[i] - m_startTicks[i]) * 1000.0 / Stopwatch.Frequency;
                    double progress = (m_latestValue[i] - m_startValue[i]) / (elapsedMs / m_samplingInterval);
                    min = Math.Min(min, progress);
                    sum += progress;
                    if (progress < 0.99)
                    {
                        below++;
                    }
                }
            }
            int measured = m_latestValue.Length - missing;
            return FormattableString.Invariant(
                $"notif/s={Interlocked.Read(ref m_notifications) / seconds:F0} progressMin={min:P2} ") +
                FormattableString.Invariant(
                $"progressMean={(measured == 0 ? 0 : sum / measured):P2} itemsBelow99={below} itemsWithoutData={missing} ") +
                (below == 0 && missing == 0 ? "PASS" : "FAIL");
        }

        private void OnDataChange(Subscription subscription, DataChangeNotification notification, ArrayOf<string> stringTable)
        {
            long now = Stopwatch.GetTimestamp();
            lock (m_lock)
            {
                foreach (MonitoredItemNotification item in notification.MonitoredItems)
                {
                    if (m_indexByHandle.TryGetValue(item.ClientHandle, out int index) &&
                        item.Value.WrappedValue.TryGetValue(out ulong value))
                    {
                        m_latestValue[index] = value;
                        m_latestTicks[index] = now;
                    }
                }
            }
            Interlocked.Add(ref m_notifications, notification.MonitoredItems.Count);
        }

        private readonly ISession m_session;
        private readonly int m_samplingInterval;
        private readonly int m_publishingInterval;
        private readonly Dictionary<uint, int> m_indexByHandle = [];
        private readonly ulong[] m_latestValue;
        private readonly long[] m_latestTicks;
        private readonly ulong[] m_startValue;
        private readonly long[] m_startTicks;
        private readonly Lock m_lock = new();
        private long m_notifications;
    }
}
