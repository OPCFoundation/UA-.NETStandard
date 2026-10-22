/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using UaLens.Subscriptions;
using UaLens.Telemetry;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class AdapterDeliveryTests
{
    [TestCase(true, 8191)]
    [TestCase(true, 8192)]
    [TestCase(true, 8193)]
    [TestCase(true, 24585)]
    [TestCase(false, 8191)]
    [TestCase(false, 8192)]
    [TestCase(false, 8193)]
    [TestCase(false, 24585)]
    public async Task QueueCountsOnlyEvictionsAtCapacityEdgesAndWraparound(bool classic, int count)
    {
        (ISubscriptionAdapter adapter, Action<NotificationEvent> write) = CreateAdapter(classic);
        await using var lifetime = adapter.ConfigureAwait(false);
        for (int index = 1; index <= count; index++)
        {
            write(Notification(index));
        }

        Assert.That(adapter.DroppedNotificationCount, Is.EqualTo(Math.Max(0, count - Capacity)));
        Assert.That(adapter.Events.Count, Is.EqualTo(Math.Min(Capacity, count)));
        Assert.That(adapter.Events.TryRead(out NotificationEvent first), Is.True);
        Assert.That(first.SequenceNumber, Is.EqualTo(Math.Max(1, count - Capacity + 1)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConsumptionAtTheCapacityBoundaryDoesNotCountAsLoss(bool classic)
    {
        (ISubscriptionAdapter adapter, Action<NotificationEvent> write) = CreateAdapter(classic);
        await using var lifetime = adapter.ConfigureAwait(false);
        for (int index = 0; index < Capacity; index++)
        {
            write(Notification(index));
        }
        for (int cycle = 0; cycle < 5; cycle++)
        {
            for (int index = 0; index < Capacity / 2; index++)
            {
                Assert.That(adapter.Events.TryRead(out _), Is.True);
            }
            for (int index = 0; index < Capacity / 2; index++)
            {
                write(Notification(index));
            }
            Assert.That(adapter.DroppedNotificationCount, Is.Zero);
        }
        for (int index = 0; index < 7; index++)
        {
            write(Notification(index));
        }
        Assert.That(adapter.DroppedNotificationCount, Is.EqualTo(7));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConcurrentWritersAndReaderAccountForEveryNotificationExactlyOnce(bool classic)
    {
        (ISubscriptionAdapter adapter, Action<NotificationEvent> write) = CreateAdapter(classic);
        Task<int> consuming = ConsumeAsync(adapter.Events);
        var producers = new Task[4];
        for (int producer = 0; producer < producers.Length; producer++)
        {
            int offset = producer * 10_000;
            producers[producer] = Task.Run(() =>
            {
                for (int index = 0; index < 10_000; index++)
                {
                    write(Notification(offset + index));
                }
            });
        }
        try
        {
            await Task.WhenAll(producers).ConfigureAwait(false);
        }
        finally
        {
            await adapter.DisposeAsync().ConfigureAwait(false);
        }
        int consumed = await consuming.ConfigureAwait(false);
        Assert.That(consumed + adapter.DroppedNotificationCount, Is.EqualTo(40_000));
        long dropped = adapter.DroppedNotificationCount;
        write(Notification(40_001));
        Assert.That(adapter.DroppedNotificationCount, Is.EqualTo(dropped),
            "A rejected write after completion is not a queue eviction.");
    }

    private static (ISubscriptionAdapter Adapter, Action<NotificationEvent> Write) CreateAdapter(bool classic)
    {
        var session = new Mock<ISession>();
        var telemetry = new AppTelemetryContext(new LogRingBuffer(16));
        if (classic)
        {
            var adapter = new ClassicEngineAdapter(session.Object, telemetry);
            return (adapter, adapter.WriteEventOrCount);
        }
        var channel = new ChannelV2EngineAdapter(session.Object, telemetry);
        return (channel, channel.WriteEventOrCount);
    }

    private static NotificationEvent Notification(int sequence)
    {
        return new NotificationEvent(NotificationKind.DataChange, 1, 1, checked((uint)sequence),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static async Task<int> ConsumeAsync(ChannelReader<NotificationEvent> reader)
    {
        int consumed = 0;
        await foreach (NotificationEvent notification in reader.ReadAllAsync().ConfigureAwait(false))
        {
            consumed++;
        }
        return consumed;
    }

    private const int Capacity = 8192;
}
