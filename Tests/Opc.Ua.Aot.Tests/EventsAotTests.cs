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

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for event monitoring operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class EventsAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task SubscribeToServerEventsAsync()
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventId"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventType"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Message"));

            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotServerEvents",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var eventItem = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                DisplayName = "ServerEvents",
                Filter = eventFilter
            };

            subscription.AddItem(eventItem);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();
            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CreateEventFilterAsync()
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventId"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("EventType"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("SourceName"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Message"));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType, QualifiedName.From("Severity"));

            await Assert.That(eventFilter.SelectClauses.Count).IsEqualTo(5);

            using var subscription = new Subscription(fixture.Session.DefaultSubscription)
            {
                DisplayName = "AotEventFilter",
                PublishingEnabled = true,
                PublishingInterval = 1000
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                DisplayName = "FilteredServerEvents",
                Filter = eventFilter
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }
    }
}
