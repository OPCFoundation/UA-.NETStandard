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
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using UaLens.Diagnostics;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class PublishLogObserverTests
{
    [Test]
    public void BurstsQueueOneBoundedDesktopDrain()
    {
        var callbacks = new Queue<Action>();
        var observer = new PublishLogObserver(callbacks.Enqueue);
        for (uint sequence = 1; sequence <= 1000; sequence++)
        {
            observer.Record(1, sequence, s_time, 1, PublishLogKind.Data);
        }

        Assert.That(callbacks, Has.Count.EqualTo(1));
        Assert.That(observer.DroppedDisplayEntries, Is.EqualTo(500));
        while (callbacks.TryDequeue(out Action? callback))
        {
            callback();
        }
        Assert.That(observer.Entries, Has.Count.EqualTo(PublishLogObserver.MaxEntries));
        Assert.That(observer.Entries[0].SequenceNumber, Is.EqualTo(501));
        Assert.That(observer.Entries[^1].SequenceNumber, Is.EqualTo(1000));
    }

    [Test]
    public void UnknownServerIdsNeverMergeDifferentPhysicalSubscriptions()
    {
        var observer = new PublishLogObserver(callback => callback());
        var first = new Mock<ISubscription>();
        var second = new Mock<ISubscription>();
        observer.RecordClient(first.Object, 0, 1, s_time, 2, PublishLogKind.Data);
        observer.RecordClient(second.Object, 0, 1, s_time, 3, PublishLogKind.Event);

        Assert.That(observer.Entries, Has.Count.EqualTo(2));
        Assert.That(observer.Entries[0].SubscriptionText, Does.StartWith("client:"));
        Assert.That(observer.Entries[0].ClientSubscriptionId,
            Is.Not.EqualTo(observer.Entries[1].ClientSubscriptionId));
    }

    [Test]
    public void MixedPayloadsOnTheSameSubscriptionRemainCorrelated()
    {
        var observer = new PublishLogObserver(callback => callback());
        var subscription = new Mock<ISubscription>();
        observer.RecordClient(subscription.Object, 42, 7, s_time, 2, PublishLogKind.Data);
        observer.RecordClient(subscription.Object, 42, 7, s_time, 3, PublishLogKind.Event);

        Assert.That(observer.Entries, Has.Count.EqualTo(1));
        Assert.That(observer.Entries[0].Kind, Is.EqualTo(PublishLogKind.Mixed));
        Assert.That(observer.Entries[0].NotifCount, Is.EqualTo(5));
        Assert.That(observer.Entries[0].SubscriptionText, Is.EqualTo("42"));
    }

    private static readonly DateTime s_time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
