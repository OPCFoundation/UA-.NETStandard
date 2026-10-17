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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises typed event observation and real data-change notifications with independently owned monitored items.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [NonParallelizable]
    public sealed class GmsResultObservationTests
    {
        /// <summary>
        /// Count completion and cancellation remove only the observation's item; raw samples retain acquisition quality.
        /// </summary>
        [Test]
        public async Task TypedObservationKeepsOtherMonitoredItemsAliveAndPreservesInitialSampleQuality()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineHandle<GMSState> machine = await fixture.Stage("ObservedGms").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()),
                out GmsResultBinding binding, new GmsResultBindingOptions
                {
                    EnableIntermediateEvents = true,
                    IncludeUsedTools = true,
                    PublishedResults = 2
                }).BuildAsync();
            ISession session = await fixture.ConnectAsync(managed: true);
            Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            await using var streaming = new StreamingSubscription(manager!,
                new SubscriptionOptions { PublishingInterval = TimeSpan.FromMilliseconds(25) });
            var slots = new List<BaseInstanceState>();
            machine.State.ResultManagement!.Results!.GetChildren(fixture.Manager.SystemContext, slots);
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using IAsyncEnumerator<DataValueChange> unrelated = streaming.SubscribeDataChangesAsync(
                slots[0].NodeId, new MonitoredItemOptions { QueueSize = 4 }, timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.That(await unrelated.MoveNextAsync(), Is.True);
            Assert.That(unrelated.Current.Value.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Task<bool> acquired = unrelated.MoveNextAsync().AsTask();
            ArrayOf<QualifiedName> names = [new QualifiedName("Bore", machine.NodeId.NamespaceIndex)];
            await using IAsyncEnumerator<GmsIntermediateResultNotification> observer =
                client.ObserveIntermediateResultsAsync(streaming, 1, names, timeout.Token).GetAsyncEnumerator(timeout.Token);
            Task<bool> notified = observer.MoveNextAsync().AsTask();
            await WaitForCreatedItemsAsync(manager!, 2, timeout.Token);
            var acquisition = new DateTimeUtc(2026, 2, 3, 4, 5, 6);
            GmsResultPublisher publisher = binding.CreatePublisher(machine);
            await publisher.PublishAsync(new GmsResult(
                GmsResultStoreTests.Result("observed").Value
                    .WithStatus(StatusCodes.UncertainDataSubNormal).WithSourceTimestamp(acquisition),
                ["probe-7"], [new GmsCharacteristic("Bore")], isIntermediate: true));

            Assert.That(await notified.WaitAsync(timeout.Token), Is.True);
            Assert.That(observer.Current.Characteristics[0].Identifier.IsNull, Is.True);
            Assert.That(observer.Current.Characteristics[0].ResultValue.IsNull, Is.True);
            Assert.That(observer.Current.Event.ProgramName, Is.Null);
            Assert.That(await observer.MoveNextAsync(), Is.False);
            Assert.That(manager!.Items.Sum(item => item.MonitoredItems.Count), Is.EqualTo(1));
            Assert.That(await acquired.WaitAsync(timeout.Token), Is.True);
            Assert.That(unrelated.Current.Value.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(unrelated.Current.Value.SourceTimestamp, Is.EqualTo(acquisition));
            await using (IAsyncEnumerator<DataValueChange> late = streaming.SubscribeDataChangesAsync(
                slots[0].NodeId, new MonitoredItemOptions { QueueSize = 2 }, timeout.Token)
                .GetAsyncEnumerator(timeout.Token))
            {
                Assert.That(await late.MoveNextAsync(), Is.True);
                Assert.That(late.Current.Value.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
                Assert.That(late.Current.Value.SourceTimestamp, Is.EqualTo(acquisition));
            }

            using var cancelObservation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            await using IAsyncEnumerator<GmsIntermediateResultNotification> canceled =
                client.ObserveIntermediateResultsAsync(streaming, 2, names, cancelObservation.Token)
                    .GetAsyncEnumerator(cancelObservation.Token);
            Task<bool> pending = canceled.MoveNextAsync().AsTask();
            await WaitForCreatedItemsAsync(manager, 2, timeout.Token);
            cancelObservation.Cancel();
            await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                Throws.InstanceOf<OperationCanceledException>());
            await canceled.DisposeAsync();
            Assert.That(manager.Items.Sum(item => item.MonitoredItems.Count), Is.EqualTo(1));

            Task<bool> next = unrelated.MoveNextAsync().AsTask();
            await publisher.PublishAsync(GmsResultStoreTests.Result("still-monitored"));
            Assert.That(await next.WaitAsync(timeout.Token), Is.True);
            Assert.That(unrelated.Current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(session.Connected, Is.True);
            Assert.That(streaming.DroppedNotificationCount, Is.Zero);
        }

        /// <summary>
        /// Waits for actual server-assigned monitored items rather than racing publication against setup.
        /// </summary>
        private static async Task WaitForCreatedItemsAsync(
            ISubscriptionManager manager,
            int count,
            CancellationToken cancellationToken)
        {
            while (manager.Items.SelectMany(item => item.MonitoredItems.Items).Count(item => item.Created) != count)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }
}
