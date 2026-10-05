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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public async Task DeleteItemsAsyncRetainsItemsWhenDeleteRequestFailsAsync()
        {
            using Subscription subscription = CreateSubscription();
            var session = new Mock<ISession>();
            session
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 7,
                    RevisedPublishingInterval = 1000,
                    RevisedMaxKeepAliveCount = 10,
                    RevisedLifetimeCount = 100
                });
            session
                .SetupSequence(s => s.DeleteMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(),
                    7,
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadRequestTimeout))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                });
            subscription.Session = session.Object;
            await subscription.CreateAsync().ConfigureAwait(false);

            MonitoredItem item = CreateItem(321u, "Created");
            item.ServerId = 654;
            subscription.AddItem(item);
            subscription.RemoveItem(item);

            Assert.ThrowsAsync<ServiceResultException>(
                () => subscription.DeleteItemsAsync());
            ArrayOf<MonitoredItem> deleted = await subscription.DeleteItemsAsync()
                .ConfigureAwait(false);

            Assert.That(deleted.Count, Is.EqualTo(1));
            Assert.That(deleted[0], Is.SameAs(item));
            session.Verify(s => s.DeleteMonitoredItemsAsync(
                It.IsAny<RequestHeader>(),
                7,
                It.Is<ArrayOf<uint>>(ids => ids.Count == 1 && ids[0] == 654),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task ConditionRefreshMethodsReturnFalseForBadMethodResultsAsync()
        {
            using Subscription subscription = CreateSubscription();
            var session = new Mock<ISession>();
            session
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 8,
                    RevisedPublishingInterval = 1000,
                    RevisedMaxKeepAliveCount = 10,
                    RevisedLifetimeCount = 100
                });
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = [new CallMethodResult { StatusCode = StatusCodes.BadMethodInvalid }],
                    DiagnosticInfos = [new DiagnosticInfo()]
                });
            subscription.Session = session.Object;
            await subscription.CreateAsync().ConfigureAwait(false);

            bool refreshResult = await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            bool refresh2Result = await subscription.ConditionRefresh2Async(123).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refreshResult, Is.False);
                Assert.That(refresh2Result, Is.False);
            });
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

        /// <summary>
        /// A restored item keeps the server id of the previous session. When
        /// the subscription is created instead of transferred, that item must
        /// still be created on the new server subscription (L7-6).
        /// </summary>
        [Test]
        public async Task CreateAsyncCreatesItemsThatCarryAStaleServerIdAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem item = CreateItem(4321u, "Restored");
            item.ServerId = 55;
            subscription.AddItem(item);
            Mock<ISession> session = CreateItemSession(7);
            subscription.Session = session.Object;

            await subscription.CreateAsync().ConfigureAwait(false);

            session.Verify(s => s.CreateMonitoredItemsAsync(
                It.IsAny<RequestHeader>(),
                7,
                It.IsAny<TimestampsToReturn>(),
                It.Is<ArrayOf<MonitoredItemCreateRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].RequestedParameters.ClientHandle == 4321u),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(item.ServerId, Is.EqualTo(100u));
        }

        /// <summary>
        /// An item removed while its create request is in flight exists on the
        /// server once the response arrives, so it must be queued for
        /// deletion instead of being leaked (L7-8).
        /// </summary>
        [Test]
        public async Task ItemRemovedDuringCreateIsQueuedForDeletionAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem item = CreateItem(4322u, "Removed");
            Mock<ISession> session = CreateItemSession(7, () => subscription.RemoveItem(item));
            session
                .Setup(s => s.DeleteMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(),
                    7,
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse { Results = [StatusCodes.Good] });
            subscription.Session = session.Object;
            await subscription.CreateAsync().ConfigureAwait(false);
            subscription.AddItem(item);

            await subscription.CreateItemsAsync().ConfigureAwait(false);
            ArrayOf<MonitoredItem> deleted = await subscription.DeleteItemsAsync().ConfigureAwait(false);

            Assert.That(deleted.Count, Is.EqualTo(1));
            Assert.That(deleted[0], Is.SameAs(item));
            session.Verify(s => s.DeleteMonitoredItemsAsync(
                It.IsAny<RequestHeader>(),
                7,
                It.Is<ArrayOf<uint>>(ids => ids.Count == 1 && ids[0] == 100u),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A saved item that was never created does not exist on the server,
        /// so it must not make the transfer of a restored subscription fail
        /// (L7-4).
        /// </summary>
        [Test]
        public async Task TransferOfRestoredSubscriptionIgnoresItemsThatWereNeverCreatedAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem created = CreateItem(4323u, "Created");
            created.ServerId = 55;
            MonitoredItem neverCreated = CreateItem(4324u, "NeverCreated");
            subscription.AddItems([created, neverCreated]);
            Mock<ISession> session = CreateGetMonitoredItemsSession([55u], [4323u]);
            subscription.Session = session.Object;

            bool transferred = await subscription.TransferAsync(session.Object, 9, [])
                .ConfigureAwait(false);

            Assert.That(transferred, Is.True);
            Assert.That(subscription.Id, Is.EqualTo(9u));
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A clone of a live subscription carries no server ids, so its items
        /// are not created locally but all exist on the server: the transfer
        /// must still succeed.
        /// </summary>
        [Test]
        public async Task TransferOfClonedSubscriptionWithoutServerIdsSucceedsAsync()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItems([CreateItem(4326u, "First"), CreateItem(4327u, "Second")]);
            Mock<ISession> session = CreateGetMonitoredItemsSession([56u, 57u], [4326u, 4327u]);
            subscription.Session = session.Object;

            bool transferred = await subscription.TransferAsync(session.Object, 9, [])
                .ConfigureAwait(false);

            Assert.That(transferred, Is.True);
            Assert.That(subscription.Id, Is.EqualTo(9u));
        }

        /// <summary>
        /// When a transferred subscription cannot be adopted, the server side
        /// subscription now owned by the session is deleted instead of being
        /// left alive as an orphan (L7-4).
        /// </summary>
        [Test]
        public async Task FailedTransferOfRestoredSubscriptionDeletesTheServerSubscriptionAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem created = CreateItem(4325u, "Created");
            created.ServerId = 55;
            subscription.AddItem(created);
            Mock<ISession> session = CreateGetMonitoredItemsSession([], []);
            subscription.Session = session.Object;

            (bool transferred, _) = await subscription.TransferWithAcknowledgementsAsync(
                session.Object, 9, [], true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(transferred, Is.False);
            Assert.That(subscription.Created, Is.False);
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.Is<ArrayOf<uint>>(ids => ids.Count == 1 && ids[0] == 9u),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The public TransferAsync is also used by ReactivateSubscriptionsAsync,
        /// which moves nothing to the session: the session's own subscription
        /// must survive a failed adoption (review G27).
        /// </summary>
        [Test]
        public async Task FailedReactivateOfRestoredSubscriptionKeepsTheServerSubscriptionAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem created = CreateItem(4325u, "Created");
            created.ServerId = 55;
            subscription.AddItem(created);
            Mock<ISession> session = CreateGetMonitoredItemsSession([], []);
            subscription.Session = session.Object;

            bool transferred = await subscription.TransferAsync(session.Object, 9, [])
                .ConfigureAwait(false);

            Assert.That(transferred, Is.False);
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A transient GetMonitoredItems error leaves the transferred
        /// subscription in place for a retry (review G27), and a rejected
        /// subscription id cannot be adopted. Any other error means the server
        /// does not implement the optional method, and the item ids the client
        /// knows complete the transfer.
        /// </summary>
        [TestCaseSource(nameof(GetMonitoredItemsFailures))]
        public async Task FailedGetMonitoredItemsFallsBackToKnownItemIdsUnlessTransientOrRejectedAsync(
            StatusCode statusCode,
            bool fails)
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem created = CreateItem(4325u, "Created");
            created.ServerId = 55;
            MonitoredItem neverCreated = CreateItem(4328u, "NeverCreated");
            subscription.AddItems([created, neverCreated]);
            Mock<ISession> session = CreateGetMonitoredItemsSession([], []);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(statusCode));
            subscription.Session = session.Object;

            (bool transferred, _) = await subscription.TransferWithAcknowledgementsAsync(
                session.Object, 9, [], true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(transferred, Is.EqualTo(!fails));
            Assert.That(subscription.Created, Is.EqualTo(!fails));
            if (!fails)
            {
                Assert.That(subscription.Id, Is.EqualTo(9u));
                Assert.That(created.Status.Id, Is.EqualTo(55u));
                Assert.That(created.ClientHandle, Is.EqualTo(4325u));
                Assert.That(neverCreated.Created, Is.False);
            }
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(s => s.CreateSubscriptionAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<bool>(),
                It.IsAny<byte>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private static IEnumerable<TestCaseData> GetMonitoredItemsFailures()
        {
            yield return new TestCaseData(StatusCodes.BadTimeout, true);
            yield return new TestCaseData(StatusCodes.BadTooManyOperations, true);
            yield return new TestCaseData(StatusCodes.BadServerTooBusy, true);
            yield return new TestCaseData(StatusCodes.BadCommunicationError, true);
            yield return new TestCaseData(StatusCodes.BadSessionIdInvalid, true);
            yield return new TestCaseData(StatusCodes.BadSubscriptionIdInvalid, true);
            yield return new TestCaseData(StatusCodes.BadMethodInvalid, false);
            yield return new TestCaseData(StatusCodes.BadNotSupported, false);
            yield return new TestCaseData(StatusCodes.BadNotImplemented, false);
            yield return new TestCaseData(StatusCodes.BadNothingToDo, false);
            yield return new TestCaseData(StatusCodes.BadInternalError, false);
        }

        /// <summary>
        /// Servers that do not implement the optional GetMonitoredItems
        /// method answer the Call with a bad operation result (asyncua:
        /// BadNothingToDo). The transfer of a subscription whose item ids
        /// are known still succeeds.
        /// </summary>
        [Test]
        public async Task TransferSucceedsWhenGetMonitoredItemsResultIsBadAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem created = CreateItem(4329u, "Created");
            created.ServerId = 77;
            subscription.AddItem(created);
            Mock<ISession> session = CreateGetMonitoredItemsSession([], []);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results = [new CallMethodResult { StatusCode = StatusCodes.BadNothingToDo }]
                });
            subscription.Session = session.Object;

            (bool transferred, _) = await subscription.TransferWithAcknowledgementsAsync(
                session.Object, 9, [], true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(transferred, Is.True);
            Assert.That(subscription.Id, Is.EqualTo(9u));
            Assert.That(created.Status.Id, Is.EqualTo(77u));
        }

        /// <summary>
        /// A clone of a live subscription carries no server ids, so without
        /// GetMonitoredItems its items could never be modified or deleted.
        /// The transferred subscription is replaced by a new one with the same
        /// items and client handles, and the transfer still succeeds.
        /// </summary>
        [Test]
        public async Task TransferOfCloneWithoutGetMonitoredItemsRecreatesTheSubscriptionAsync()
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem first = CreateItem(4330u, "First");
            MonitoredItem second = CreateItem(4331u, "Second");
            subscription.AddItems([first, second]);
            Mock<ISession> session = CreateItemSession(21);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadMethodInvalid));
            session
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse { Results = [StatusCodes.Good] });
            subscription.Session = session.Object;

            (bool transferred, ArrayOf<uint> acknowledgements) = await subscription
                .TransferWithAcknowledgementsAsync(session.Object, 9, [3u, 4u], true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(transferred, Is.True);
            Assert.That(acknowledgements.Count, Is.Zero);
            Assert.That(subscription.Id, Is.EqualTo(21u));
            Assert.That(first.Created && second.Created, Is.True);
            Assert.That(first.ClientHandle, Is.EqualTo(4330u));
            Assert.That(second.ClientHandle, Is.EqualTo(4331u));
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.Is<ArrayOf<uint>>(ids => ids.Count == 1 && ids[0] == 9u),
                It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.CreateMonitoredItemsAsync(
                It.IsAny<RequestHeader>(),
                21u,
                It.IsAny<TimestampsToReturn>(),
                It.Is<ArrayOf<MonitoredItemCreateRequest>>(r => r.Count == 2 &&
                    r[0].RequestedParameters.ClientHandle == 4330u &&
                    r[1].RequestedParameters.ClientHandle == 4331u),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The old subscription must be gone before the replacement is
        /// created, otherwise both deliver every notification. When the server
        /// does not confirm the delete the transfer fails without a create.
        /// </summary>
        [Test]
        public async Task CloneIsNotRecreatedWhenTheTransferredSubscriptionCannotBeDeletedAsync()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItem(CreateItem(4333u, "First"));
            Mock<ISession> session = CreateItemSession(23);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadMethodInvalid));
            session
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse { Results = [StatusCodes.BadSubscriptionIdInvalid] });
            subscription.Session = session.Object;

            (bool transferred, _) = await subscription.TransferWithAcknowledgementsAsync(
                session.Object, 9, [], true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(transferred, Is.False);
            Assert.That(subscription.Created, Is.False);
            session.Verify(s => s.CreateSubscriptionAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<bool>(),
                It.IsAny<byte>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A failed create of the replacement fails the transfer instead of
        /// throwing out of it.
        /// </summary>
        [Test]
        public async Task FailedRecreateOfCloneFailsTheTransferAsync()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItem(CreateItem(4334u, "First"));
            Mock<ISession> session = CreateGetMonitoredItemsSession([], []);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadNothingToDo));
            session
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadTooManySubscriptions));
            subscription.Session = session.Object;

            (bool transferred, _) = await subscription.TransferWithAcknowledgementsAsync(
                session.Object, 9, [], true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(transferred, Is.False);
            Assert.That(subscription.Created, Is.False);
        }

        /// <summary>
        /// A Good GetMonitoredItems answer that does not carry two UInt32
        /// arrays of the same length is unusable: the classic engine falls
        /// back to the known item ids instead of throwing or failing.
        /// </summary>
        [TestCaseSource(nameof(UnusableGetMonitoredItemsOutputs))]
        public async Task UnusableGetMonitoredItemsOutputFallsBackToKnownItemIdsAsync(
            Variant serverHandles,
            Variant clientHandles)
        {
            using Subscription subscription = CreateSubscription();
            MonitoredItem created = CreateItem(4335u, "Created");
            created.ServerId = 88;
            subscription.AddItem(created);
            Mock<ISession> session = CreateGetMonitoredItemsSession([], []);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = [serverHandles, clientHandles]
                        }
                    ]
                });
            subscription.Session = session.Object;

            (bool transferred, _) = await subscription.TransferWithAcknowledgementsAsync(
                session.Object, 9, [], true, CancellationToken.None).ConfigureAwait(false);

            Assert.That(transferred, Is.True);
            Assert.That(subscription.Id, Is.EqualTo(9u));
            Assert.That(created.Status.Id, Is.EqualTo(88u));
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private static IEnumerable<TestCaseData> UnusableGetMonitoredItemsOutputs()
        {
            yield return new TestCaseData(
                Variant.From(new string[] { "88" }.ToArrayOf()),
                Variant.From(new uint[] { 4335u }.ToArrayOf()));
            yield return new TestCaseData(
                Variant.From(new uint[] { 88u, 89u }.ToArrayOf()),
                Variant.From(new uint[] { 4335u }.ToArrayOf()));
        }

        /// <summary>
        /// Without a TransferSubscriptions call by the caller (the public
        /// TransferAsync, used to adopt the session's own subscriptions) a
        /// clone without GetMonitoredItems is not replaced: the server
        /// subscription is neither deleted nor recreated.
        /// </summary>
        [Test]
        public async Task AdoptingCloneWithoutGetMonitoredItemsFailsWithoutRecreateAsync()
        {
            using Subscription subscription = CreateSubscription();
            subscription.AddItem(CreateItem(4332u, "First"));
            Mock<ISession> session = CreateItemSession(22);
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadMethodInvalid));
            subscription.Session = session.Object;

            bool transferred = await subscription.TransferAsync(session.Object, 9, [])
                .ConfigureAwait(false);

            Assert.That(transferred, Is.False);
            Assert.That(subscription.Created, Is.False);
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(s => s.CreateSubscriptionAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<double>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<bool>(),
                It.IsAny<byte>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private static Mock<ISession> CreateItemSession(uint subscriptionId, Action onCreateItems = null)
        {
            var session = new Mock<ISession>();
            session
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = subscriptionId,
                    RevisedPublishingInterval = 1000,
                    RevisedMaxKeepAliveCount = 10,
                    RevisedLifetimeCount = 100
                });
            session
                .Setup(s => s.CreateMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(),
                    subscriptionId,
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((
                    RequestHeader _,
                    uint _,
                    TimestampsToReturn _,
                    ArrayOf<MonitoredItemCreateRequest> requests,
                    CancellationToken _) =>
                {
                    onCreateItems?.Invoke();
                    var results = new MonitoredItemCreateResult[requests.Count];
                    for (int ii = 0; ii < results.Length; ii++)
                    {
                        results[ii] = new MonitoredItemCreateResult
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 100u + (uint)ii
                        };
                    }
                    return new CreateMonitoredItemsResponse
                    {
                        Results = new ArrayOf<MonitoredItemCreateResult>(results)
                    };
                });
            return session;
        }

        private static Mock<ISession> CreateGetMonitoredItemsSession(
            ArrayOf<uint> serverHandles,
            ArrayOf<uint> clientHandles)
        {
            var session = new Mock<ISession>();
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = [Variant.From(serverHandles), Variant.From(clientHandles)]
                        }
                    ]
                });
            session
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse { Results = [StatusCodes.Good] });
            return session;
        }
    }
}
