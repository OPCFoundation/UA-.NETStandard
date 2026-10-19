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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers relative-path identity and asynchronous lifecycle boundaries.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Subscription")]
    public sealed class ClassicRelativePathRestorationTests
    {
        /// <summary>
        /// Snapshots retain a known identity and legacy restores clear obsolete metadata.
        /// </summary>
        [Test]
        public async Task SnapshotRestoresOriginalTargetWithoutBrowsingAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription origin = CreateSubscription(session);
            MonitoredItem item = CreateItem();
            origin.AddItem(item);
            await origin.CreateAsync().ConfigureAwait(false);
            item.Snapshot(out MonitoredItemState state);
            var restored = new MonitoredItem(NUnitTelemetryContext.Create());

            restored.Restore(state);

            Assert.That(restored.ResolvedNodeId, Is.EqualTo(new NodeId("Target", 1)));
            Assert.That(restored.Status.NodeId, Is.EqualTo(new NodeId("Target", 1)));
            Assert.That(restored.ServerId, Is.EqualTo(item.ServerId));
            Assert.That(restored.ClientHandle, Is.EqualTo(item.ClientHandle));

            restored.Restore(new MonitoredItemState
            {
                StartNodeId = ObjectIds.ObjectsFolder,
                RelativePath = "/Old",
                ServerId = 700,
                ClientId = 800
            });
            Assert.That(restored.ResolvedNodeId.IsNull, Is.True);
            Assert.That(restored.Status.NodeId.IsNull, Is.True);
            VerifyTranslationCount(session, Times.Once());
        }

        /// <summary>
        /// Replacement server items use the current path rather than a saved target.
        /// </summary>
        [Test]
        public async Task RecreationResolvesThePathAgainRatherThanUsingSavedIdentityAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription origin = CreateSubscription(session);
            MonitoredItem item = CreateItem();
            origin.AddItem(item);
            await origin.CreateAsync().ConfigureAwait(false);
            origin.Snapshot(out SubscriptionState state);
            using Subscription restored = CreateSubscription(session);
            restored.Restore(state);
            SetTranslation(session, Task.FromResult(Translation("NewTarget")));

            await restored.CreateAsync().ConfigureAwait(false);

            MonitoredItem created = restored.MonitoredItems.Single();
            Assert.That(created.ResolvedNodeId, Is.EqualTo(new NodeId("NewTarget", 1)));
            Assert.That(created.Status.NodeId, Is.EqualTo(new NodeId("NewTarget", 1)));
            VerifyTranslationCount(session, Times.Exactly(2));
        }

        /// <summary>
        /// An existing item with unknown metadata does not enter the new-item batch.
        /// </summary>
        [Test]
        public async Task UnknownLegacyTargetDoesNotBlockAddingAnotherRelativePathItemAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            MonitoredItem legacy = CreateItem();
            subscription.AddItem(legacy);
            await subscription.CreateAsync().ConfigureAwait(false);
            uint serverId = legacy.ServerId;
            legacy.Restore(new MonitoredItemState
            {
                StartNodeId = ObjectIds.ObjectsFolder,
                RelativePath = "/Legacy",
                ServerId = serverId,
                ClientId = legacy.ClientHandle
            });
            MonitoredItem added = CreateItem();
            subscription.AddItem(added);

            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            Assert.That(legacy.ServerId, Is.EqualTo(serverId));
            Assert.That(legacy.ResolvedNodeId.IsNull, Is.True);
            Assert.That(added.Created, Is.True);
            VerifyTranslationCount(session, Times.Exactly(2));
            VerifyCreateCount(session, Times.Exactly(2));
        }

        /// <summary>
        /// A failed creation's translated target is not persisted as an existing server identity.
        /// </summary>
        [Test]
        public async Task UncreatedItemSnapshotDoesNotRetainTranslatedTargetAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            session.Setup(s => s.CreateMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results = [new MonitoredItemCreateResult { StatusCode = StatusCodes.BadTooManyMonitoredItems }]
                });
            await subscription.CreateItemsAsync().ConfigureAwait(false);
            Assert.That(item.Created, Is.False);
            Assert.That(item.ResolvedNodeId, Is.EqualTo(new NodeId("Target", 1)));

            item.Snapshot(out MonitoredItemState state);
            var restored = new MonitoredItem(NUnitTelemetryContext.Create());
            restored.Restore(state);

            Assert.That(restored.ResolvedNodeId.IsNull, Is.True);
            Mock<ISession> targetSession = CreateSession();
            SetTranslation(targetSession, Task.FromResult(Translation("ChangedTarget")));
            using Subscription target = CreateSubscription(targetSession);
            await target.CreateAsync().ConfigureAwait(false);
            target.AddItem(restored);
            await target.ApplyChangesAsync().ConfigureAwait(false);
            Assert.That(restored.Created, Is.True);
            Assert.That(restored.Status.NodeId, Is.EqualTo(new NodeId("ChangedTarget", 1)));
            VerifyTranslationCount(targetSession, Times.Once());
        }

        /// <summary>
        /// A malformed sibling path does not abort creation or poison later apply retries.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MalformedPathDoesNotBlockValidSiblingCreationAsync(bool createSubscription)
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            if (!createSubscription)
            {
                await subscription.CreateAsync().ConfigureAwait(false);
            }
            MonitoredItem malformed = CreateItem();
            malformed.RelativePath = "<";
            MonitoredItem valid = CreateItem();
            subscription.AddItems([malformed, valid]);

            if (createSubscription)
            {
                await subscription.CreateAsync().ConfigureAwait(false);
            }
            else
            {
                await subscription.ApplyChangesAsync().ConfigureAwait(false);
            }

            Assert.That(subscription.Created, Is.True);
            Assert.That(valid.Created, Is.True);
            Assert.That(valid.Status.NodeId, Is.EqualTo(new NodeId("Target", 1)));
            Assert.That(malformed.Created, Is.False);
            Assert.That(ServiceResult.IsBad(malformed.Status.Error), Is.True);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            Assert.That(ServiceResult.IsBad(malformed.Status.Error), Is.True);
            VerifyCreateCount(session, Times.Once());
            session.Verify(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<uint>>(),
                It.IsAny<CancellationToken>()), Times.Never);

            malformed.RelativePath = "/Fixed";
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            Assert.That(malformed.Created, Is.True);
            Assert.That(ServiceResult.IsGood(malformed.Status.Error), Is.True);
            VerifyCreateCount(session, Times.Exactly(2));
        }

        /// <summary>
        /// Monitoring options changed during translation are sent in the creation request.
        /// </summary>
        [Test]
        public async Task CreationUsesMonitoringOptionsChangedDuringTranslationAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            var translation = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetTranslation(session, translation.Task);
            Task<ArrayOf<MonitoredItem>> creating = subscription.CreateItemsAsync();
            try
            {
                VerifyTranslationCount(session, Times.Once());
                item.SamplingInterval = 321;
                item.QueueSize = 17;
                item.DiscardOldest = false;
                item.Filter = new DataChangeFilter { DeadbandValue = 2.5 };
            }
            finally
            {
                translation.TrySetResult(Translation("Target"));
                await creating.ConfigureAwait(false);
            }

            session.Verify(s => s.CreateMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<TimestampsToReturn>(),
                It.Is<ArrayOf<MonitoredItemCreateRequest>>(requests =>
                    requests.Count == 1 &&
                    requests[0].ItemToMonitor.NodeId == new NodeId("Target", 1) &&
                    requests[0].RequestedParameters.SamplingInterval == 321 &&
                    requests[0].RequestedParameters.QueueSize == 17 &&
                    !requests[0].RequestedParameters.DiscardOldest),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(item.Status.Filter, Is.TypeOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)item.Status.Filter!).DeadbandValue, Is.EqualTo(2.5));
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            session.Verify(s => s.ModifyMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A paused translation cannot admit retargeting or duplicate creation.
        /// </summary>
        [Test]
        public async Task CreationReservesTargetBeforeAwaitingTranslationAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            var translation = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetTranslation(session, translation.Task);

            Task<ArrayOf<MonitoredItem>> first = subscription.CreateItemsAsync();
            try
            {
                VerifyTranslationCount(session, Times.Once());
                ServiceResultException? error = Assert.Throws<ServiceResultException>(
                    () => item.RelativePath = "/Other");
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.Throws<ServiceResultException>(() => item.StartNodeId = ObjectIds.Server);
                Assert.DoesNotThrow(() => item.RelativePath = "/Child");
                Assert.That((await subscription.CreateItemsAsync().ConfigureAwait(false)).IsEmpty, Is.True);
                VerifyTranslationCount(session, Times.Once());
            }
            finally
            {
                translation.TrySetResult(Translation("Target"));
                await first.ConfigureAwait(false);
            }

            Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("Target", 1)));
            VerifyCreateCount(session, Times.Once());
        }

        /// <summary>
        /// Configuration changes, including an ABA change, invalidate earlier translations.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SupersededExplicitResolutionCannotInstallAStaleTargetAsync(bool changeBack)
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            var translation = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetTranslation(session, translation.Task);
            Task resolving = subscription.ResolveItemNodeIdsAsync();

            item.RelativePath = "/Other";
            if (changeBack)
            {
                item.RelativePath = "/Child";
            }
            translation.SetResult(Translation("Stale"));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resolving.ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);

            SetTranslation(session, Task.FromResult(Translation("Current")));
            await subscription.CreateItemsAsync().ConfigureAwait(false);
            Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("Current", 1)));
        }

        /// <summary>
        /// Explicit resolution cannot overwrite a newer creation's target.
        /// </summary>
        [Test]
        public async Task ExplicitResolutionCannotOverwriteAConcurrentCreationAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            var translation = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetTranslation(session, translation.Task);
            Task resolving = subscription.ResolveItemNodeIdsAsync();
            SetTranslation(session, Task.FromResult(Translation("CreatedTarget")));
            await subscription.CreateItemsAsync().ConfigureAwait(false);

            translation.SetResult(Translation("Stale"));

            await Assert.ThrowsAsync<ServiceResultException>(async () => await resolving.ConfigureAwait(false));
            Assert.That(item.ResolvedNodeId, Is.EqualTo(new NodeId("CreatedTarget", 1)));
            Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("CreatedTarget", 1)));
        }

        /// <summary>
        /// Preparation failure does not leave an item permanently reserved.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task TranslationFailureReleasesCreationReservationForRetryAsync(bool cancel)
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            var translation = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetTranslation(session, translation.Task);
            Task<ArrayOf<MonitoredItem>> creating = subscription.CreateItemsAsync();
            if (cancel)
            {
                translation.SetCanceled();
                await Assert.ThrowsAsync<TaskCanceledException>(async () => await creating.ConfigureAwait(false));
            }
            else
            {
                translation.SetException(new ServiceResultException(StatusCodes.BadCommunicationError));
                await Assert.ThrowsAsync<ServiceResultException>(async () => await creating.ConfigureAwait(false));
            }

            Assert.DoesNotThrow(() => item.RelativePath = "/Retry");
            SetTranslation(session, Task.FromResult(Translation("Retry")));
            await subscription.CreateItemsAsync().ConfigureAwait(false);

            Assert.That(item.Created, Is.True);
            Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("Retry", 1)));
            VerifyCreateCount(session, Times.Once());
        }

        /// <summary>
        /// Invalid response cardinality releases the reservation for a later retry.
        /// </summary>
        [Test]
        public async Task InvalidTranslationResponseReleasesCreationReservationAsync()
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            await subscription.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            SetTranslation(session, Task.FromResult(new TranslateBrowsePathsToNodeIdsResponse { Results = [] }));

            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await subscription.CreateItemsAsync().ConfigureAwait(false));

            Assert.DoesNotThrow(() => item.RelativePath = "/Retry");
            SetTranslation(session, Task.FromResult(Translation("Retry")));
            await subscription.CreateItemsAsync().ConfigureAwait(false);
            Assert.That(item.Created, Is.True);
        }

        /// <summary>
        /// Rejected deletes retain identity until the server confirms absence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedDeletionRetainsIdentityUntilConfirmedAbsentAsync(bool alreadyAbsent)
        {
            StatusCode finalStatus = alreadyAbsent ? StatusCodes.BadMonitoredItemIdInvalid : StatusCodes.Good;
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            await subscription.CreateAsync().ConfigureAwait(false);
            uint serverId = item.ServerId;
            session.SetupSequence(s => s.DeleteMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse { Results = [StatusCodes.BadUserAccessDenied] })
                .ReturnsAsync(new DeleteMonitoredItemsResponse { Results = [finalStatus] });
            subscription.RemoveItem(item);

            await subscription.DeleteItemsAsync().ConfigureAwait(false);

            Assert.That(item.ServerId, Is.EqualTo(serverId));
            Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("Target", 1)));
            Assert.That(item.Status.Error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.Throws<ServiceResultException>(() => item.RelativePath = "/New");
            ArrayOf<MonitoredItem> retried = await subscription.DeleteItemsAsync().ConfigureAwait(false);
            Assert.That(retried.Count, Is.EqualTo(1));
            Assert.That(item.ServerId, Is.Zero);
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);
            Assert.That(item.Status.NodeId.IsNull, Is.True);
            Assert.DoesNotThrow(() => item.RelativePath = "/New");
        }

        /// <summary>
        /// Losing a subscription clears retained identities without reactivating removed items.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SubscriptionResetClearsFailedDeletionIdentityAsync(bool recreateSession)
        {
            Mock<ISession> session = CreateSession();
            using Subscription subscription = CreateSubscription(session);
            MonitoredItem item = CreateItem();
            subscription.AddItem(item);
            await subscription.CreateAsync().ConfigureAwait(false);
            session.Setup(s => s.DeleteMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse { Results = [StatusCodes.BadUserAccessDenied] });
            session.Setup(s => s.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse { Results = [StatusCodes.Good] });
            subscription.RemoveItem(item);
            await subscription.DeleteItemsAsync().ConfigureAwait(false);
            Assert.That(item.Created, Is.True);
            Assert.That(subscription.ChangesPending, Is.True);

            if (recreateSession)
            {
                await subscription.ResetForSessionRecreateAsync().ConfigureAwait(false);
            }
            else
            {
                await subscription.DeleteAsync(false).ConfigureAwait(false);
            }

            Assert.That(item.ServerId, Is.Zero);
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);
            Assert.That(item.Status.NodeId.IsNull, Is.True);
            Assert.That(subscription.MonitoredItems, Is.Empty);
            Assert.That(subscription.ChangesPending, Is.False);
            Assert.DoesNotThrow(() => item.RelativePath = "/Replacement");
            await subscription.CreateAsync().ConfigureAwait(false);
            Assert.That((await subscription.DeleteItemsAsync().ConfigureAwait(false)).IsEmpty, Is.True);
            VerifyCreateCount(session, Times.Once());
        }

        /// <summary>
        /// Moving an item, even away and back, invalidates its previous owner's translation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task OwnershipChangeRejectsPendingResolutionAsync(bool moveBack)
        {
            Mock<ISession> firstSession = CreateSession();
            Mock<ISession> secondSession = CreateSession();
            using Subscription first = CreateSubscription(firstSession);
            using Subscription second = CreateSubscription(secondSession);
            await first.CreateAsync().ConfigureAwait(false);
            await second.CreateAsync().ConfigureAwait(false);
            MonitoredItem item = CreateItem();
            first.AddItem(item);
            var translation = new TaskCompletionSource<TranslateBrowsePathsToNodeIdsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetTranslation(firstSession, translation.Task);
            Task resolving = first.ResolveItemNodeIdsAsync();

            first.RemoveItem(item);
            second.AddItem(item);
            if (moveBack)
            {
                second.RemoveItem(item);
                first.AddItem(item);
            }
            translation.SetResult(Translation("StaleOwner"));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await resolving.ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);
            Mock<ISession> currentSession = moveBack ? firstSession : secondSession;
            Subscription current = moveBack ? first : second;
            SetTranslation(currentSession, Task.FromResult(Translation("CurrentOwner")));
            await current.CreateItemsAsync().ConfigureAwait(false);
            Assert.That(item.Created, Is.True);
            Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("CurrentOwner", 1)));
        }

        /// <summary>
        /// Creates a classic subscription using the service boundary under test.
        /// </summary>
        private static Subscription CreateSubscription(Mock<ISession> session)
        {
            return new Subscription(NUnitTelemetryContext.Create()) { Session = session.Object };
        }

        /// <summary>
        /// Creates a relative-path item without a resolved cache.
        /// </summary>
        private static MonitoredItem CreateItem()
        {
            return new MonitoredItem(NUnitTelemetryContext.Create())
            {
                StartNodeId = ObjectIds.ObjectsFolder,
                RelativePath = "/Child"
            };
        }

        /// <summary>
        /// Supplies deterministic service responses and unique server handles.
        /// </summary>
        private static Mock<ISession> CreateSession()
        {
            var session = new Mock<ISession>();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:restoration:test");
            session.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            session.SetupGet(s => s.TypeTree).Returns(new TypeTable(namespaces));
            session.Setup(s => s.CreateSubscriptionAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<uint>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<bool>(), It.IsAny<byte>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 9,
                    RevisedPublishingInterval = 1000,
                    RevisedMaxKeepAliveCount = 10,
                    RevisedLifetimeCount = 100
                });
            uint nextId = 100;
            session.Setup(s => s.CreateMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader _, uint _, TimestampsToReturn _,
                    ArrayOf<MonitoredItemCreateRequest> requests, CancellationToken _) =>
                    new CreateMonitoredItemsResponse
                    {
                        Results = requests.ConvertAll(_ => new MonitoredItemCreateResult
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = nextId++
                        })
                    });
            SetTranslation(session, Task.FromResult(Translation("Target")));
            return session;
        }

        /// <summary>
        /// Returns one fully resolved target in the fixture namespace.
        /// </summary>
        private static TranslateBrowsePathsToNodeIdsResponse Translation(string target)
        {
            return new TranslateBrowsePathsToNodeIdsResponse
            {
                Results =
                [
                    new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets =
                        [
                            new BrowsePathTarget
                            {
                                TargetId = new ExpandedNodeId(target, 1),
                                RemainingPathIndex = uint.MaxValue
                            }
                        ]
                    }
                ]
            };
        }

        /// <summary>
        /// Controls when the translation service completes.
        /// </summary>
        private static void SetTranslation(
            Mock<ISession> session, Task<TranslateBrowsePathsToNodeIdsResponse> response)
        {
            session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(response));
        }

        /// <summary>
        /// Verifies translation traffic at the public service boundary.
        /// </summary>
        private static void VerifyTranslationCount(Mock<ISession> session, Times times)
        {
            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies creation traffic at the public service boundary.
        /// </summary>
        private static void VerifyCreateCount(Mock<ISession> session, Times times)
        {
            session.Verify(s => s.CreateMonitoredItemsAsync(
                It.IsAny<RequestHeader>(), 9, It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()), times);
        }
    }
}
