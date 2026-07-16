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
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Subscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ClassicSubscriptionCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        private Subscription CreateSubscription()
        {
            return new Subscription(m_telemetry);
        }

        private MonitoredItem CreateItem(uint clientHandle, string displayName)
        {
            return new MonitoredItem(clientHandle, m_telemetry) { DisplayName = displayName };
        }

        [Test]
        public void DefaultConstructorSetsExpectedDefaults()
        {
            using Subscription subscription = CreateSubscription();

            Assert.Multiple(() =>
            {
                Assert.That(subscription.DisplayName, Is.EqualTo("Subscription"));
                Assert.That(subscription.PublishingInterval, Is.Zero);
                Assert.That(subscription.KeepAliveCount, Is.Zero);
                Assert.That(subscription.LifetimeCount, Is.Zero);
                Assert.That(subscription.MaxNotificationsPerPublish, Is.Zero);
                Assert.That(subscription.PublishingEnabled, Is.False);
                Assert.That(subscription.Priority, Is.Zero);
                Assert.That(subscription.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Both));
                Assert.That(subscription.MaxMessageCount, Is.EqualTo(10));
                Assert.That(subscription.MinLifetimeInterval, Is.Zero);
                Assert.That(subscription.Id, Is.Zero);
                Assert.That(subscription.Created, Is.False);
                Assert.That(subscription.MonitoredItemCount, Is.Zero);
                Assert.That(subscription.Session, Is.Null);
                Assert.That(subscription.ChangesPending, Is.False);
                Assert.That(subscription.DefaultItem, Is.Not.Null);
                Assert.That(subscription.SequenceNumber, Is.Zero);
                Assert.That(subscription.NotificationCount, Is.Zero);
                Assert.That(subscription.LastNotification, Is.Null);
                Assert.That(subscription.Notifications, Is.Empty);
                Assert.That(subscription.AvailableSequenceNumbers.IsEmpty, Is.True);
            });
        }

        [Test]
        public void ConstructorWithOptionsAppliesProvidedValues()
        {
            var options = new SubscriptionOptions
            {
                DisplayName = "Values",
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 30,
                MaxNotificationsPerPublish = 5,
                PublishingEnabled = true,
                Priority = 42,
                TimestampsToReturn = TimestampsToReturn.Source,
                MaxMessageCount = 25,
                MinLifetimeInterval = 12000
            };

            using var subscription = new Subscription(m_telemetry, options);

            Assert.Multiple(() =>
            {
                Assert.That(subscription.DisplayName, Is.EqualTo("Values"));
                Assert.That(subscription.PublishingInterval, Is.EqualTo(1000));
                Assert.That(subscription.KeepAliveCount, Is.EqualTo(10u));
                Assert.That(subscription.LifetimeCount, Is.EqualTo(30u));
                Assert.That(subscription.MaxNotificationsPerPublish, Is.EqualTo(5u));
                Assert.That(subscription.PublishingEnabled, Is.True);
                Assert.That(subscription.Priority, Is.EqualTo(42));
                Assert.That(subscription.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Source));
                Assert.That(subscription.MaxMessageCount, Is.EqualTo(25));
                Assert.That(subscription.MinLifetimeInterval, Is.EqualTo(12000u));
            });
        }

        [Test]
        public void ObsoleteParameterlessConstructorProducesDefaults()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            using var subscription = new Subscription();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.Multiple(() =>
            {
                Assert.That(subscription.DisplayName, Is.EqualTo("Subscription"));
                Assert.That(subscription.MaxMessageCount, Is.EqualTo(10));
                Assert.That(subscription.DefaultItem, Is.Not.Null);
            });
        }

        [Test]
        public void PropertySettersRoundTripExpectedValues()
        {
            using Subscription subscription = CreateSubscription();
            object handle = new();

            subscription.DisplayName = "Custom";
            subscription.PublishingInterval = 2000;
            subscription.KeepAliveCount = 15;
            subscription.LifetimeCount = 45;
            subscription.MaxNotificationsPerPublish = 8;
            subscription.PublishingEnabled = true;
            subscription.Priority = 7;
            subscription.TimestampsToReturn = TimestampsToReturn.Neither;
            subscription.MaxMessageCount = 33;
            subscription.MinLifetimeInterval = 9000;
            subscription.DisableMonitoredItemCache = true;
            subscription.SequentialPublishing = true;
            subscription.RepublishAfterTransfer = true;
            subscription.TransferId = 4321;
            subscription.Handle = handle;

            Assert.Multiple(() =>
            {
                Assert.That(subscription.DisplayName, Is.EqualTo("Custom"));
                Assert.That(subscription.PublishingInterval, Is.EqualTo(2000));
                Assert.That(subscription.KeepAliveCount, Is.EqualTo(15u));
                Assert.That(subscription.LifetimeCount, Is.EqualTo(45u));
                Assert.That(subscription.MaxNotificationsPerPublish, Is.EqualTo(8u));
                Assert.That(subscription.PublishingEnabled, Is.True);
                Assert.That(subscription.Priority, Is.EqualTo(7));
                Assert.That(subscription.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Neither));
                Assert.That(subscription.MaxMessageCount, Is.EqualTo(33));
                Assert.That(subscription.MinLifetimeInterval, Is.EqualTo(9000u));
                Assert.That(subscription.DisableMonitoredItemCache, Is.True);
                Assert.That(subscription.SequentialPublishing, Is.True);
                Assert.That(subscription.RepublishAfterTransfer, Is.True);
                Assert.That(subscription.TransferId, Is.EqualTo(4321u));
                Assert.That(subscription.Handle, Is.SameAs(handle));
            });
        }

        [Test]
        public void AddItemAssignsSubscriptionAndTracksItem()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem item = CreateItem(101u, "Item101");

            subscription.AddItem(item);

            Assert.Multiple(() =>
            {
                Assert.That(subscription.MonitoredItemCount, Is.EqualTo(1u));
                Assert.That(item.Subscription, Is.SameAs(subscription));
                Assert.That(subscription.FindItemByClientHandle(101u), Is.SameAs(item));
                Assert.That(subscription.MonitoredItems, Has.Member(item));
            });
        }

        [Test]
        public void AddItemNullThrowsArgumentNullException()
        {
            using Subscription subscription = CreateSubscription();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => subscription.AddItem(null));

            Assert.That(ex.ParamName, Is.EqualTo("monitoredItem"));
        }

        [Test]
        public void AddItemDuplicateClientHandleIsIgnored()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItem(CreateItem(202u, "First"));

            subscription.AddItem(CreateItem(202u, "Second"));

            Assert.Multiple(() =>
            {
                Assert.That(subscription.MonitoredItemCount, Is.EqualTo(1u));
                Assert.That(subscription.FindItemByClientHandle(202u).DisplayName, Is.EqualTo("First"));
            });
        }

        [Test]
        public void AddItemsAddsEveryItem()
        {
            using Subscription subscription = CreateSubscription();

            subscription.AddItems([CreateItem(1u, "A"), CreateItem(2u, "B"), CreateItem(3u, "C")]);

            Assert.That(subscription.MonitoredItemCount, Is.EqualTo(3u));
        }

        [Test]
        public void AddItemsNullThrowsArgumentNullException()
        {
            using Subscription subscription = CreateSubscription();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => subscription.AddItems(null));

            Assert.That(ex.ParamName, Is.EqualTo("monitoredItems"));
        }

        [Test]
        public void RemoveItemClearsSubscriptionReference()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem item = CreateItem(303u, "Item303");
            subscription.AddItem(item);

            subscription.RemoveItem(item);

            Assert.Multiple(() =>
            {
                Assert.That(subscription.MonitoredItemCount, Is.Zero);
                Assert.That(item.Subscription, Is.Null);
                Assert.That(subscription.FindItemByClientHandle(303u), Is.Null);
            });
        }

        [Test]
        public void RemoveItemNullThrowsArgumentNullException()
        {
            using Subscription subscription = CreateSubscription();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => subscription.RemoveItem(null));

            Assert.That(ex.ParamName, Is.EqualTo("monitoredItem"));
        }

        [Test]
        public void RemoveItemNotPresentIsNoOp()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem item = CreateItem(404u, "Absent");

            Assert.DoesNotThrow(() => subscription.RemoveItem(item));
            Assert.That(subscription.MonitoredItemCount, Is.Zero);
        }

        [Test]
        public void RemoveItemsRemovesEveryPresentItem()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem first = CreateItem(11u, "First");
            MonitoredItem second = CreateItem(12u, "Second");
            subscription.AddItems([first, second]);

            subscription.RemoveItems([first, second]);

            Assert.That(subscription.MonitoredItemCount, Is.Zero);
        }

        [Test]
        public void RemoveItemsNullThrowsArgumentNullException()
        {
            using Subscription subscription = CreateSubscription();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => subscription.RemoveItems(null));

            Assert.That(ex.ParamName, Is.EqualTo("monitoredItems"));
        }

        [Test]
        public void FindItemByClientHandleReturnsNullWhenAbsent()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(subscription.FindItemByClientHandle(9999u), Is.Null);
        }

        [Test]
        public void ChangesPendingTrueWhenItemHasModifiedAttributes()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItem(CreateItem(55u, "Pending"));

            Assert.That(subscription.ChangesPending, Is.True);
        }

        [Test]
        public void AddItemRaisesStateChangedWithItemsAddedMask()
        {
            using Subscription subscription = CreateSubscription();
            SubscriptionChangeMask captured = SubscriptionChangeMask.None;
            subscription.StateChanged += (_, e) => captured = e.Status;

            subscription.AddItem(CreateItem(77u, "Notify"));

            Assert.That(captured, Is.EqualTo(SubscriptionChangeMask.ItemsAdded));
        }

        [Test]
        public void RemoveItemRaisesStateChangedWithItemsRemovedMask()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem item = CreateItem(78u, "Remove");
            subscription.AddItem(item);
            SubscriptionChangeMask captured = SubscriptionChangeMask.None;
            subscription.StateChanged += (_, e) => captured = e.Status;

            subscription.RemoveItem(item);

            Assert.That(captured, Is.EqualTo(SubscriptionChangeMask.ItemsRemoved));
        }

        [Test]
        public void ChangesCompletedResetsChangeMaskToNone()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItem(CreateItem(79u, "Reset"));
            SubscriptionChangeMask captured = SubscriptionChangeMask.ItemsAdded;
            subscription.StateChanged += (_, e) => captured = e.Status;

            subscription.ChangesCompleted();

            Assert.That(captured, Is.EqualTo(SubscriptionChangeMask.None));
        }

        [Test]
        public void CloneCopiesStateAndItems()
        {
            using Subscription subscription = CreateSubscription();
            subscription.DisplayName = "Original";
            subscription.PublishingInterval = 500;
            subscription.AddItem(CreateItem(30u, "Item30"));

            using var clone = (Subscription)subscription.Clone();

            Assert.Multiple(() =>
            {
                Assert.That(clone.DisplayName, Is.EqualTo("Original"));
                Assert.That(clone.PublishingInterval, Is.EqualTo(500));
                Assert.That(clone.MonitoredItemCount, Is.EqualTo(1u));
                Assert.That(clone.MonitoredItems.First().DisplayName, Is.EqualTo("Item30"));
            });
        }

        [Test]
        public void MemberwiseCloneReturnsIndependentSubscription()
        {
            using Subscription subscription = CreateSubscription();
            subscription.DisplayName = "Source";

            using var clone = (Subscription)subscription.MemberwiseClone();
            clone.DisplayName = "Changed";

            Assert.Multiple(() =>
            {
                Assert.That(subscription.DisplayName, Is.EqualTo("Source"));
                Assert.That(clone.DisplayName, Is.EqualTo("Changed"));
            });
        }

        [Test]
        public void CloneSubscriptionCopiesItems()
        {
            using Subscription subscription = CreateSubscription();
            subscription.DisplayName = "Templated";
            subscription.AddItem(CreateItem(31u, "Item31"));

            using Subscription clone = subscription.CloneSubscription(true);

            Assert.Multiple(() =>
            {
                Assert.That(clone.DisplayName, Is.EqualTo("Templated"));
                Assert.That(clone.MonitoredItemCount, Is.EqualTo(1u));
            });
        }

        [Test]
        public void TemplateConstructorNullThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new Subscription((Subscription)null));

            Assert.That(ex.ParamName, Is.EqualTo("template"));
        }

        [Test]
        public void SnapshotAndRestoreRoundTripItemsAndCurrentValues()
        {
            using var subscription = new Subscription(m_telemetry)
            {
                CurrentPublishingInterval = 1234.5,
                CurrentKeepAliveCount = 7,
                CurrentLifetimeCount = 21
            };
            subscription.AddItems([CreateItem(10u, "A"), CreateItem(20u, "B")]);

            subscription.Snapshot(out SubscriptionState state);

            using Subscription restored = CreateSubscription();
            restored.Restore(state);

            Assert.Multiple(() =>
            {
                Assert.That(restored.MonitoredItemCount, Is.EqualTo(2u));
                Assert.That(restored.CurrentPublishingInterval, Is.EqualTo(1234.5));
                Assert.That(restored.CurrentKeepAliveCount, Is.EqualTo(7u));
                Assert.That(restored.CurrentLifetimeCount, Is.EqualTo(21u));
            });
        }

        [Test]
        public void SessionSetterRoundTripsAssignedSession()
        {
            using Subscription subscription = CreateSubscription();
            ISession session = new Mock<ISession>().Object;

            subscription.Session = session;

            Assert.That(subscription.Session, Is.SameAs(session));
        }

        [Test]
        public void CreateAsyncWithoutSessionThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.CreateAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void CreateItemsAsyncWithoutSessionThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.CreateItemsAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ModifyAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.ModifyAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void SetPublishingModeAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.SetPublishingModeAsync(true));

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ResolveItemNodeIdsAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.ResolveItemNodeIdsAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ModifyItemsAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.ModifyItemsAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void DeleteItemsAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.DeleteItemsAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ResendDataAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.ResendDataAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void ConditionRefreshAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.ConditionRefreshAsync());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void RepublishAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.RepublishAsync(1));

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void SetMonitoringModeAsyncWhenNotCreatedThrowsInvalidState()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.SetMonitoringModeAsync(MonitoringMode.Reporting, []));

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void DeleteAsyncSilentWhenNotCreatedDoesNotThrow()
        {
            using Subscription subscription = CreateSubscription();

            Assert.DoesNotThrowAsync(() => subscription.DeleteAsync(true));
            Assert.That(subscription.Created, Is.False);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            Subscription subscription = CreateSubscription();

            subscription.Dispose();

            Assert.DoesNotThrow(subscription.Dispose);
        }
    }
}
