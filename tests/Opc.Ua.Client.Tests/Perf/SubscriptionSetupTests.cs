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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Perf.ServerLoadHarness;
using ManagedSubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ManagedSubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace Opc.Ua.Client.Tests.Perf
{
    /// <summary>
    /// Exercises the readiness loop used by the managed client load harness.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SubscriptionSetupTests
    {
        /// <summary>
        /// A ready subscription does not wait for the polling interval.
        /// </summary>
        [Test]
        public async Task ReadySubscriptionCompletesImmediatelyAsync()
        {
            var setup = new SubscriptionSetup();
            Mock<ManagedSubscription> subscription = CreateSubscription(true, CreateItem(true).Object);

            Task wait = setup.WaitForReadyAsync(subscription.Object, CancellationToken.None);

            Assert.That(wait.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            await wait.ConfigureAwait(false);
        }

        /// <summary>
        /// Readiness requires both the subscription and every monitored item.
        /// </summary>
        [Test]
        public async Task WaitsForTheSubscriptionAndAllItemsAsync()
        {
            var clock = new FakeTimeProvider();
            var setup = new SubscriptionSetup(clock);
            Mock<IMonitoredItem> item = CreateItem(false);
            Mock<ManagedSubscription> subscription = CreateSubscription(false, item.Object);
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Task wait = setup.WaitForReadyAsync(subscription.Object, guard.Token);
            Assert.That(wait.IsCompleted, Is.False);
            subscription.SetupGet(value => value.Created).Returns(true);
            Assert.That(wait.IsCompleted, Is.False);
            item.SetupGet(value => value.Created).Returns(true);
            clock.Advance(TimeSpan.FromMilliseconds(50));

            await wait.ConfigureAwait(false);
        }

        /// <summary>
        /// A bad item must fail setup, even when another item is pending or the bad item is created.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RejectedMonitoredItemFailsSetup(bool created)
        {
            var setup = new SubscriptionSetup();
            Mock<IMonitoredItem> rejected = CreateItem(created);
            rejected.SetupGet(value => value.Error).Returns(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            Mock<ManagedSubscription> subscription = CreateSubscription(
                true, CreateItem(false).Object, rejected.Object);
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => setup.WaitForReadyAsync(subscription.Object, guard.Token))!;

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
            Assert.That(exception.Message, Does.Contain("item"));
        }

        /// <summary>
        /// Error and deletion state notifications fail setup instead of waiting for creation.
        /// </summary>
        [TestCase(ManagedSubscriptionState.Error)]
        [TestCase(ManagedSubscriptionState.Deleted)]
        public void FailedSubscriptionStateFailsSetup(ManagedSubscriptionState state)
        {
            var setup = new SubscriptionSetup();
            Mock<ManagedSubscription> subscription = CreateSubscription(false, CreateItem(false).Object);
            setup.OnStateChanged(state);
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => setup.WaitForReadyAsync(subscription.Object, guard.Token))!;

            Assert.That(exception.Message, Does.Contain(state.ToString()));
        }

        /// <summary>
        /// An error delivered while polling remains visible even if the engine starts recovering.
        /// </summary>
        [Test]
        public void ErrorDuringSetupIsNotLostDuringRecovery()
        {
            var clock = new FakeTimeProvider();
            var setup = new SubscriptionSetup(clock);
            Mock<ManagedSubscription> subscription = CreateSubscription(false, CreateItem(false).Object);
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task wait = setup.WaitForReadyAsync(subscription.Object, guard.Token);

            setup.OnStateChanged(ManagedSubscriptionState.Error);
            setup.OnStateChanged(ManagedSubscriptionState.Opened);
            clock.Advance(TimeSpan.FromMilliseconds(50));

            Assert.ThrowsAsync<InvalidOperationException>(() => wait);
        }

        /// <summary>
        /// Neither missing subscription creation nor missing item creation can wait indefinitely.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SetupHasAFiniteDeadline(bool subscriptionCreated)
        {
            var clock = new FakeTimeProvider();
            var setup = new SubscriptionSetup(clock);
            Mock<ManagedSubscription> subscription = CreateSubscription(
                subscriptionCreated, CreateItem(false).Object);
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task wait = setup.WaitForReadyAsync(subscription.Object, guard.Token);

            clock.Advance(TimeSpan.FromMinutes(1));

            TimeoutException exception = Assert.ThrowsAsync<TimeoutException>(() => wait)!;
            Assert.That(exception.Message, Does.Contain("subscription"));
        }

        /// <summary>
        /// Caller cancellation is preserved, including when the subscription is already ready.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void CallerCancellationInterruptsSetup(bool ready)
        {
            var setup = new SubscriptionSetup();
            Mock<ManagedSubscription> subscription = CreateSubscription(ready, CreateItem(ready).Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                () => setup.WaitForReadyAsync(subscription.Object, cancellation.Token));
        }

        private static Mock<ManagedSubscription> CreateSubscription(bool created, params IMonitoredItem[] items)
        {
            var collection = new Mock<IMonitoredItemCollection>();
            collection.SetupGet(value => value.Items).Returns(items);
            var subscription = new Mock<ManagedSubscription>();
            subscription.SetupGet(value => value.Created).Returns(created);
            subscription.SetupGet(value => value.MonitoredItems).Returns(collection.Object);
            return subscription;
        }

        private static Mock<IMonitoredItem> CreateItem(bool created)
        {
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(value => value.Name).Returns("item");
            item.SetupGet(value => value.Created).Returns(created);
            item.SetupGet(value => value.Error).Returns(ServiceResult.Good);
            return item;
        }
    }
}
