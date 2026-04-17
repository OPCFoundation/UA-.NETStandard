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
    /// AOT integration tests for subscription lifecycle operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class SubscriptionAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task CreateAndDeleteSubscriptionAsync()
        {
            using var subscription = new Subscription(fixture.Session!.DefaultSubscription)
            {
                DisplayName = "AotCreateDelete",
                PublishingEnabled = true,
                PublishingInterval = 500,
                KeepAliveCount = 2
            };

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "CurrentTime",
                SamplingInterval = 250
            };

            subscription.AddItem(item);
            fixture.Session.AddSubscription(subscription);

            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();
            await Assert.That((int)subscription.MonitoredItemCount)
                .IsEqualTo(1);
            await Assert.That(item.Status.Created).IsTrue();

            // Delete the subscription
            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsFalse();
        }

        [Test]
        public async Task ModifySubscriptionAsync()
        {
            using var subscription = new Subscription(fixture.Session!.DefaultSubscription)
            {
                DisplayName = "AotModify",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            fixture.Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();

            double originalInterval = subscription.CurrentPublishingInterval;

            // Modify the publishing interval
            subscription.PublishingInterval = 2000;
            await subscription.ModifyAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.CurrentPublishingInterval)
                .IsNotEqualTo(originalInterval);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task TransferSubscriptionAsync()
        {
            // Create source session with a subscription
            ISession sourceSession = await fixture.CreateSessionAsync("TransferSource")
                .ConfigureAwait(false);

            using var subscription = new Subscription(sourceSession.DefaultSubscription)
            {
                DisplayName = "AotTransfer",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 5
            };

            sourceSession.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "CurrentTime"
            };

            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(subscription.Created).IsTrue();

            // Create target session
            ISession targetSession = await fixture.CreateSessionAsync("TransferTarget")
                .ConfigureAwait(false);

            // Clone subscriptions for transfer
            var sourceSubscriptions =
                new SubscriptionCollection(sourceSession.Subscriptions);
            SubscriptionCollection transferSubscriptions =
                sourceSubscriptions.CloneSubscriptions(false);

            foreach (Subscription s in transferSubscriptions)
            {
                targetSession.AddSubscription(s);
            }

            // Exercise the TransferSubscriptions service call.
            // The result depends on server state; for AOT testing the
            // important thing is that the serialization code path runs.
            await targetSession.TransferSubscriptionsAsync(
                transferSubscriptions, true, CancellationToken.None)
                .ConfigureAwait(false);

            // Cleanup
            targetSession.DeleteSubscriptionsOnClose = true;
            await targetSession.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            targetSession.Dispose();

            sourceSession.DeleteSubscriptionsOnClose = true;
            await sourceSession.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            sourceSession.Dispose();
        }

        [Test]
        public async Task KeepAliveAsync()
        {
            using var subscription = new Subscription(fixture.Session!.DefaultSubscription)
            {
                DisplayName = "AotKeepAlive",
                PublishingEnabled = true,
                PublishingInterval = 500,
                KeepAliveCount = 1
            };

            int keepAliveCount = 0;
            subscription.FastKeepAliveCallback = (_, _) =>
                Interlocked.Increment(ref keepAliveCount);

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_State,
                AttributeId = Attributes.Value,
                DisplayName = "ServerState"
            };

            subscription.AddItem(item);
            fixture.Session.AddSubscription(subscription);

            await subscription.CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await subscription.ApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // Wait for keep-alive notifications
            await Task.Delay(3000).ConfigureAwait(false);

            await Assert.That(keepAliveCount).IsGreaterThan(0);

            await fixture.Session.RemoveSubscriptionAsync(subscription)
                .ConfigureAwait(false);
        }
    }
}
