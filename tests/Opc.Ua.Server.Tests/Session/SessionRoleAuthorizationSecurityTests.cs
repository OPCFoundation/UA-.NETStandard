/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Covers activation, live-role epochs and retained notifications using real socket-free services.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class SessionRoleAuthorizationSecurityTests
    {
        /// <summary>
        /// Background revocation is effective in the real event queue before another service request or Publish.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR36RevokedEventCannotEnterRealQueueBeforeNextRequestAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateEventSubscriptionAsync(true).ConfigureAwait(false);
            using OperationContext owner = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false);
            await harness.ReportEventAsync("event-42").ConfigureAwait(false);
            Assert.That(harness.EventReadyToPublish, Is.True);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            NotificationMessage initial = subscription.Publish(owner, out _, out _)!;
            AssertEvent(initial, "event-42");
            Assert.That(subscription.Acknowledge(owner, initial.SequenceNumber), Is.Null);
            Assert.That(harness.EventReadyToPublish, Is.False);

            harness.SetObserver(false);
            await harness.ReportEventAsync("secret-after-revocation").ConfigureAwait(false);
            Assert.That(harness.EventReadyToPublish, Is.False);

            harness.SetObserver(true);
            await harness.ReportEventAsync("new-after-regrant").ConfigureAwait(false);
            Assert.That(harness.EventReadyToPublish, Is.True);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            using OperationContext currentOwner = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false);
            AssertEvent(subscription.Publish(currentOwner, out _, out _)!, "new-after-regrant");
        }

        /// <summary>
        /// A separately activated Sign session cannot acquire an encrypted notifier through subscription transfer.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR32EventTransferCannotRemoveNotifierEncryptionAsync(bool namespacePolicy)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateEventSubscriptionAsync(true, namespacePolicy)
                .ConfigureAwait(false);
            (CreateSessionResult destination, SecureChannelContext channel) =
                await harness.CreateDestinationAsync(false).ConfigureAwait(false);
            using OperationContext context = await harness.AdmitAsync(
                destination, channel, RequestType.TransferSubscriptions).ConfigureAwait(false);
            TransferSubscriptionsResponse transfer = await harness.TransferAsync(context, subscription.Id)
                .ConfigureAwait(false);
            Assert.That(transfer.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(subscription.Session, Is.SameAs(harness.Created.Session));
            await harness.ReportEventAsync("ordinary-secret").ConfigureAwait(false);
            Assert.That(() => subscription.Publish(context, out _, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            using OperationContext owner = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            AssertEvent(subscription.Publish(owner, out _, out _)!, "ordinary-secret");
        }

        /// <summary>
        /// Adequately protected transfers and deliberately unrestricted Sign notifiers retain ordinary event delivery.
        /// </summary>
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        [TestCase(false, false, false)]
        [Category("SecurityRegression")]
        public async Task SecurityR32CompatibleTransferAndUnrestrictedSignEventsRemainUsableAsync(
            bool restricted, bool encryptedDestination, bool namespacePolicy)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateEventSubscriptionAsync(restricted, namespacePolicy)
                .ConfigureAwait(false);
            (CreateSessionResult destination, SecureChannelContext channel) =
                await harness.CreateDestinationAsync(encryptedDestination).ConfigureAwait(false);
            using OperationContext context = await harness.AdmitAsync(
                destination, channel, RequestType.TransferSubscriptions).ConfigureAwait(false);
            TransferSubscriptionsResponse transfer = await harness.TransferAsync(context, subscription.Id)
                .ConfigureAwait(false);
            Assert.That(transfer.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(subscription.Session, Is.SameAs(destination.Session));
            await harness.ReportEventAsync("event-42").ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            using OperationContext publication = await harness.AdmitAsync(destination, channel, RequestType.Publish)
                .ConfigureAwait(false);
            AssertEvent(subscription.Publish(publication, out _, out _)!, "event-42");
        }

        /// <summary>
        /// A value-only item transfers without event permissions and preserves queued and subsequent values.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR32ValueOnlyTransferDoesNotRequireReceiveEventsAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateValueSubscriptionAsync().ConfigureAwait(false);
            await harness.QueueValueAsync(42).ConfigureAwait(false);
            (CreateSessionResult destination, SecureChannelContext channel) =
                await harness.CreateDestinationAsync(false, sameChannel: true).ConfigureAwait(false);
            using OperationContext transfer = await harness.AdmitAsync(
                destination, channel, RequestType.TransferSubscriptions).ConfigureAwait(false);

            TransferSubscriptionsResponse result = await harness.TransferAsync(transfer, subscription.Id)
                .ConfigureAwait(false);

            Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(subscription.Session, Is.SameAs(destination.Session));
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            using OperationContext publication = await harness.AdmitAsync(
                destination, channel, RequestType.Publish).ConfigureAwait(false);
            NotificationMessage? message = subscription.Publish(publication, out _, out _);
            Assert.That(message, Is.Not.Null);
            AssertValue(message!, 42);
            Assert.That(subscription.Acknowledge(publication, message!.SequenceNumber), Is.Null);

            await harness.QueueValueAsync(43).ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            NotificationMessage? subsequent = subscription.Publish(publication, out _, out _);
            Assert.That(subsequent, Is.Not.Null);
            AssertValue(subsequent!, 43);
        }

        /// <summary>
        /// Anonymous notification continuity requires the same application, mapped identity, and complete role set.
        /// </summary>
        [TestCase("urn:owner", "urn:owner", 0, true, true)]
        [TestCase("urn:owner", "urn:owner", 1, true, true)]
        [TestCase("urn:owner", "urn:owner", 2, true, false)]
        [TestCase("urn:owner", "urn:owner", 3, true, true)]
        [TestCase("urn:owner", "urn:owner", 4, true, false)]
        [TestCase("urn:owner", "urn:owner", 5, true, false)]
        [TestCase("urn:owner", "urn:owner", 6, true, true)]
        [TestCase("urn:owner", "urn:other", 0, true, false)]
        [TestCase("urn:owner", null, 0, true, false)]
        [TestCase("urn:owner", "", 0, true, false)]
        [TestCase(null, null, 0, true, false)]
        [TestCase("", "", 0, true, false)]
        [TestCase("urn:owner", "urn:owner", 0, false, false)]
        [Category("SecurityRegression")]
        public void SecurityR37AnonymousNotificationContinuityRequiresSameApplicationAndGrants(
            string? ownerUri, string? destinationUri, int roleVariation, bool sameEffectiveIdentity, bool equivalent)
        {
            var owner = new UserIdentity();
            var destination = new UserIdentity();
            var namespaces = new NamespaceTable();
            var ownerEffective = new RoleBasedIdentity(owner, [Role.Observer, Role.Operator], namespaces);
            Role[] destinationRoles = roleVariation switch
            {
                1 => [Role.Operator, Role.Observer],
                2 => [Role.Observer],
                3 => [Role.Observer, Role.Operator, Role.ConfigureAdmin],
                4 => [Role.Observer, Role.Engineer],
                5 => [],
                6 => [Role.Observer, Role.Operator, Role.Observer],
                _ => [Role.Observer, Role.Operator]
            };
            IUserIdentity destinationIdentity = sameEffectiveIdentity ? destination : Mock.Of<IUserIdentity>();
            var destinationEffective = new RoleBasedIdentity(
                destinationIdentity, destinationRoles, namespaces);
            var original = new NotificationAuthorization(owner.TokenHandler, owner, ownerEffective, ownerUri);
            var replacement = new NotificationAuthorization(
                destination.TokenHandler, destination, destinationEffective, destinationUri);
            bool sameKnownApplication = !string.IsNullOrEmpty(ownerUri) &&
                string.Equals(ownerUri, destinationUri, StringComparison.Ordinal);
            bool reverseEquivalent = sameEffectiveIdentity &&
                sameKnownApplication &&
                ((roleVariation is 2 or 5) || (roleVariation is not 3 && equivalent));

            Assert.That(original.IsEquivalentTo(replacement), Is.EqualTo(equivalent));
            Assert.That(replacement.IsEquivalentTo(original), Is.EqualTo(reverseEquivalent));
        }

        /// <summary>
        /// Tightening only notifier restrictions after a valid transfer prevents the next ordinary event.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR32EventDeliveryRechecksExplicitRestrictionsAsync(bool namespacePolicy)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateEventSubscriptionAsync(false, namespacePolicy)
                .ConfigureAwait(false);
            (CreateSessionResult destination, SecureChannelContext channel) =
                await harness.CreateDestinationAsync(false).ConfigureAwait(false);
            using OperationContext context = await harness.AdmitAsync(
                destination, channel, RequestType.TransferSubscriptions).ConfigureAwait(false);
            Assert.That((await harness.TransferAsync(context, subscription.Id).ConfigureAwait(false))
                .Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            await harness.ReportEventAsync("event-42").ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            using (OperationContext publication = await harness.AdmitAsync(
                destination, channel, RequestType.Publish).ConfigureAwait(false))
            {
                NotificationMessage message = subscription.Publish(publication, out _, out _)!;
                AssertEvent(message, "event-42");
                Assert.That(subscription.Acknowledge(publication, message.SequenceNumber), Is.Null);
            }
            harness.RequireNotifierEncryption(namespacePolicy);
            await harness.ReportEventAsync("secret-after-tightening").ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            subscription.PublishTimerExpired();
            using OperationContext after = await harness.AdmitAsync(destination, channel, RequestType.Publish)
                .ConfigureAwait(false);
            NotificationMessage? next = subscription.Publish(after, out _, out _);
            Assert.That(next == null || next.NotificationData.Count == 0, Is.True);
        }

        /// <summary>
        /// A role removed before the session's first ordinary request cannot survive activation-time resolution.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR35RevocationBeforeFirstOrdinaryRequestCannotPreserveRoleAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Assert.That(harness.Created.Session.EffectiveIdentity.GrantedRoleIds.ToArray(),
                Does.Contain(ObjectIds.WellKnownRole_Observer));
            harness.SetObserver(false);

            DataValue denied = await harness.ReadAsync().ConfigureAwait(false);

            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(denied.WrappedValue.IsNull, Is.True);
            Assert.That(harness.ValueReads, Is.Zero);
            Assert.That(harness.Created.Session.Activated, Is.True);
        }

        /// <summary>
        /// Unchanged mappings and subsequent legitimate grants remain usable without terminating the session.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR35RoleListenerAttachesBeforeGrantsAndKeepsValidRolesAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Assert.That((await harness.ReadAsync().ConfigureAwait(false)).WrappedValue, Is.EqualTo(new Variant(42)));
            harness.SetObserver(false);
            Assert.That((await harness.ReadAsync().ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.SetObserver(true);
            DataValue allowed = await harness.ReadAsync().ConfigureAwait(false);
            Assert.That(allowed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(allowed.WrappedValue, Is.EqualTo(new Variant(42)));
            Assert.That(harness.ValueReads, Is.EqualTo(2));
            Assert.That(harness.Created.Session.Activated, Is.True);
        }

        /// <summary>
        /// A change after resolution but before Session.Activate cannot mark the earlier role set fresh.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR45RoleRevocationBetweenResolutionAndActivationCommitCannotPersistAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Assert.That((await harness.ReadAsync().ConfigureAwait(false)).WrappedValue, Is.EqualTo(new Variant(42)));
            harness.Sessions.AfterResolution = () => harness.SetObserver(false);

            await harness.ActivateAsync("alice").ConfigureAwait(false);
            DataValue denied = await harness.ReadAsync().ConfigureAwait(false);

            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(denied.WrappedValue.IsNull, Is.True);
            Assert.That(harness.ValueReads, Is.EqualTo(1));
            Assert.That(harness.Created.Session.EffectiveIdentity.GrantedRoleIds.ToArray(),
                Does.Not.Contain(ObjectIds.WellKnownRole_Observer));
        }

        /// <summary>
        /// An unchanged role epoch preserves the exact identity and protected result across reactivation.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR45UnchangedRoleEpochCommitsExpectedIdentityAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            ArrayOf<NodeId> before = harness.Created.Session.EffectiveIdentity.GrantedRoleIds;
            await harness.ActivateAsync("alice").ConfigureAwait(false);
            Assert.That(harness.Created.Session.EffectiveIdentity.GrantedRoleIds.ToArray(),
                Is.EquivalentTo(before.ToArray()!));
            DataValue value = await harness.ReadAsync().ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(42)));
            Assert.That(harness.ValueReads, Is.EqualTo(1));
        }

        /// <summary>
        /// Old unsent messages cannot be published after a real same-channel principal change.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR37ChangedUserCannotPublishPrebuiltUnsentNotificationsAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateValueSubscriptionAsync().ConfigureAwait(false);
            await harness.QueueValueAsync(70).ConfigureAwait(false);
            await harness.QueueValueAsync(71).ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            uint previousSequence;
            using (OperationContext context = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false))
            {
                NotificationMessage first = subscription.Publish(context, out _, out bool more)!;
                Assert.That(first, Is.Not.Null);
                Assert.That(more, Is.True, "The limit of one must leave prebuilt unsent messages.");
                previousSequence = first.SequenceNumber;
            }
            await harness.ActivateAsync("mallory").ConfigureAwait(false);
            using (OperationContext context = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false))
            {
                NotificationMessage? next = subscription.Publish(context, out ArrayOf<uint> available, out _);
                AssertNoProtectedValue(next);
                Assert.That(available.ToArray(), Does.Not.Contain(previousSequence));
            }
        }

        /// <summary>
        /// Retained unacknowledged bytes cease to be retransmittable when the principal changes.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR37ChangedUserCannotRepublishPreviousPrincipalPayloadAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateValueSubscriptionAsync().ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            uint sequence;
            using (OperationContext context = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false))
            {
                NotificationMessage original = subscription.Publish(context, out _, out _)!;
                Assert.That(original.NotificationData, Is.Not.Empty);
                sequence = original.SequenceNumber;
                Assert.That(subscription.Republish(context, sequence).SequenceNumber, Is.EqualTo(sequence));
            }
            await harness.ActivateAsync("mallory").ConfigureAwait(false);
            using OperationContext denied = await harness.AdmitAsync(RequestType.Republish).ConfigureAwait(false);
            Assert.That(() => subscription.Republish(denied, sequence),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadMessageNotAvailable));
        }

        /// <summary>
        /// A changed user keeps an active subscription: acknowledging a message the client already
        /// received succeeds and later values are delivered (CTT Session Change User 001).
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR37ChangedUserAcknowledgesDeliveredMessageAndKeepsPublishingAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateValueSubscriptionAsync().ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            uint sequence;
            using (OperationContext context = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false))
            {
                NotificationMessage original = subscription.Publish(context, out _, out _)!;
                sequence = original.SequenceNumber;
                AssertValue(original, 42);
            }
            harness.SetObserver(true, "mallory");
            await harness.ActivateAsync("mallory").ConfigureAwait(false);
            await harness.QueueValueAsync(43).ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            using OperationContext next = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false);
            ServiceResult? acknowledged = subscription.Acknowledge(next, sequence);
            NotificationMessage? message = subscription.Publish(next, out _, out _);
            Assert.That(message, Is.Not.Null);
            AssertValue(message!, 43);
            Assert.That(acknowledged, Is.Null);
        }

        /// <summary>
        /// Reactivation of the same principal retains sequence, payload and acknowledgement semantics.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR37SamePrincipalReactivationPreservesRetransmissionContractAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            Subscription subscription = await harness.CreateValueSubscriptionAsync().ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(subscription.PublishTimerExpired(), Is.EqualTo(PublishingState.NotificationsAvailable));
            uint sequence;
            using (OperationContext context = await harness.AdmitAsync(RequestType.Publish).ConfigureAwait(false))
            {
                NotificationMessage original = subscription.Publish(context, out _, out _)!;
                sequence = original.SequenceNumber;
                AssertValue(original, 42);
            }
            await harness.ActivateAsync("alice").ConfigureAwait(false);
            using OperationContext repeated = await harness.AdmitAsync(RequestType.Republish).ConfigureAwait(false);
            NotificationMessage retransmitted = subscription.Republish(repeated, sequence);
            Assert.That(retransmitted.SequenceNumber, Is.EqualTo(sequence));
            AssertValue(retransmitted, 42);
            Assert.That(subscription.Acknowledge(repeated, sequence), Is.Null);
            Assert.That(() => subscription.Republish(repeated, sequence),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadMessageNotAvailable));
        }

        /// <summary>
        /// Permits a fresh denial notification but never retained successful bytes from the previous principal.
        /// </summary>
        private static void AssertNoProtectedValue(NotificationMessage? message)
        {
            if (message == null)
            {
                return;
            }
            foreach (ExtensionObject notification in message.NotificationData)
            {
                Assert.That(notification.TryGetValue(out DataChangeNotification? data), Is.True);
                foreach (MonitoredItemNotification item in data!.MonitoredItems)
                {
                    Assert.That(item.Value.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(item.Value.WrappedValue.IsNull, Is.True);
                }
            }
        }

        /// <summary>
        /// Checks the actual encoded notification's one retained value.
        /// </summary>
        private static void AssertValue(NotificationMessage message, int expected)
        {
            Assert.That(message.NotificationData, Has.Count.EqualTo(1));
            Assert.That(message.NotificationData[0].TryGetValue(out DataChangeNotification? data), Is.True);
            Assert.That(data!.MonitoredItems, Has.Count.EqualTo(1));
            Assert.That(data.MonitoredItems[0].Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(data.MonitoredItems[0].Value.WrappedValue, Is.EqualTo(new Variant(expected)));
        }

        /// <summary>
        /// Checks the real filtered event notification rather than the authorization helper alone.
        /// </summary>
        private static void AssertEvent(NotificationMessage message, string expected)
        {
            Assert.That(message, Is.Not.Null);
            Assert.That(message.NotificationData, Has.Count.EqualTo(1));
            Assert.That(message.NotificationData[0].TryGetValue(out EventNotificationList? events), Is.True);
            Assert.That(events!.Events, Has.Count.EqualTo(1));
            Assert.That(events.Events[0].EventFields, Has.Count.EqualTo(1));
            Assert.That(events.Events[0].EventFields[0].TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo(expected));
        }

        /// <summary>
        /// Owns real sessions, roles, dispatcher, node and subscription services without network listeners.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Wires all services with TLS endpoint evidence and a fixed clock.
            /// </summary>
            public Harness()
            {
                m_server = DeterministicServerMock.Create(out m_queues, Clock);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_server.SetupGet(value => value.Telemetry).Returns(telemetry);
                m_server.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
                var types = (TypeTable)m_server.Object.TypeTree;
                types.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);
                types.AddSubtype(ObjectTypeIds.BaseEventType, ObjectTypeIds.BaseObjectType);
                types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(DataTypeIds.Number, DataTypeIds.BaseDataType);
                types.AddSubtype(DataTypeIds.Integer, DataTypeIds.Number);
                types.AddSubtype(DataTypeIds.Int32, DataTypeIds.Integer);
                m_events = new EventManager(m_server.Object, 100, 100);
                m_server.SetupGet(value => value.EventManager).Returns(m_events);
                m_server.SetupGet(value => value.RoleManager).Returns(m_roles);
                m_server.SetupGet(value => value.IdentityRegistry).Returns(new ServerIdentityRegistry(
                    new UserNamePasswordAuthenticator((handler, _) =>
                        new ValueTask<IUserIdentity>(new UserIdentity(handler)))));
                SetObserver(true);
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                uint nextId = 0;
                diagnostics.Setup(value => value.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(), It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(), It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(), It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<NodeId>(new NodeId(++nextId, 1)));
                diagnostics.Setup(value => value.CreateSubscriptionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(), It.IsAny<SubscriptionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>()))
                    .Returns(() => new ValueTask<NodeId>(new NodeId(++nextId, 1)));
                m_server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics.Object);
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MinSessionTimeout = 1000, MaxSessionTimeout = 60_000, MaxSessionCount = 10,
                        MinPublishingInterval = 100, MaxPublishingInterval = 10_000,
                        MinSubscriptionLifetime = 1000, MaxSubscriptionLifetime = 60_000,
                        MaxMessageQueueSize = 10, MaxNotificationsPerPublish = 10,
                        MaxSubscriptionCount = 10, MaxMonitoredItemsPerSubscription = 100
                    }
                };
                m_resources = new ResourceManager(configuration);
                m_server.SetupGet(value => value.ResourceManager).Returns(m_resources);
                Sessions = new RoleRaceSessionManager(m_server.Object, configuration, Clock);
                m_server.SetupGet(value => value.SessionManager).Returns(Sessions);
                m_owner = new TestableAsyncCustomNodeManager(m_server.Object, configuration, false,
                    NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                m_value = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("RoleProtectedValue", m_owner.NamespaceIndex),
                    BrowseName = new QualifiedName("RoleProtectedValue", m_owner.NamespaceIndex),
                    DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar, Value = 42,
                    AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
                    RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)PermissionType.Read
                        }
                    ]
                };
                m_value.OnSimpleReadValue = (ISystemContext _, NodeState _, ref Variant _) =>
                {
                    ValueReads++;
                    return ServiceResult.Good;
                };
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(m_server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(m_server.Object.CoreNodeManager);
                m_server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                m_dispatcher = new MasterNodeManager(m_server.Object, configuration, null, [m_owner], null);
                m_server.SetupGet(value => value.NodeManager).Returns(m_dispatcher);
                m_subscriptions = new SubscriptionManager(m_server.Object, configuration, Clock);
                m_server.SetupGet(value => value.SubscriptionManager).Returns(m_subscriptions);
                m_certificate = CertificateBuilder.Create("CN=Session Role Regression")
                    .SetRSAKeySize(2048).CreateForRSA();
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = "https://localhost/role-regression",
                    TransportProfileUri = Profiles.HttpsBinaryTransport,
                    SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "username", TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                };
                m_channel = new SecureChannelContext("role-regression", endpoint, RequestEncoding.Binary);
            }

            /// <summary>
            /// Gets the deterministic subscription and session clock.
            /// </summary>
            public FakeTimeProvider Clock { get; } = new();

            /// <summary>
            /// Gets the manager exposing only the resolution/commit boundary.
            /// </summary>
            public RoleRaceSessionManager Sessions { get; }

            /// <summary>
            /// Gets the actual activated session.
            /// </summary>
            public CreateSessionResult Created { get; private set; } = null!;

            /// <summary>
            /// Gets the number of protected Value reads that reached the provider.
            /// </summary>
            public int ValueReads { get; private set; }

            /// <summary>
            /// Gets the actual event monitored item's readiness, without admitting another request.
            /// </summary>
            public bool EventReadyToPublish => m_events.GetMonitoredItems()[0].IsReadyToPublish;

            /// <summary>
            /// Registers the variable and activates alice without making an ordinary request.
            /// </summary>
            public async Task InitializeAsync()
            {
                await m_owner.AddNodeAsync(m_owner.SystemContext, NodeId.Null, m_value).ConfigureAwait(false);
                using var context = new OperationContext(new RequestHeader(), m_channel,
                    RequestType.CreateSession, RequestLifetime.None);
                Created = await Sessions.CreateSessionAsync(context, m_certificate, "role-regression", default,
                    new ApplicationDescription(), m_channel.EndpointDescription!.EndpointUrl, null, [],
                    60_000, 65_536).ConfigureAwait(false);
                await ActivateAsync("alice").ConfigureAwait(false);
            }

            /// <summary>
            /// Changes the sole observer mapping using the real role manager.
            /// </summary>
            public void SetObserver(bool granted, string userName = "alice")
            {
                var rule = new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName, Criteria = userName
                };
                ServiceResult result = granted
                    ? m_roles.AddIdentity(ObjectIds.WellKnownRole_Observer, rule)
                    : m_roles.RemoveIdentity(ObjectIds.WellKnownRole_Observer, rule);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            }

            /// <summary>
            /// Performs real username authentication and activation on the same confidential channel.
            /// </summary>
            public async Task ActivateAsync(string userName)
            {
                using var context = new OperationContext(new RequestHeader(), m_channel,
                    RequestType.ActivateSession, RequestLifetime.None);
                await Sessions.ActivateSessionAsync(context, Created.AuthenticationToken, new SignatureData(),
                    new ExtensionObject(new UserNameIdentityToken
                    {
                        PolicyId = "username", UserName = userName, Password = ByteString.From([1])
                    }), new SignatureData(), []).ConfigureAwait(false);
            }

            /// <summary>
            /// Runs normal session request admission before service dispatch.
            /// </summary>
            public ValueTask<OperationContext> AdmitAsync(RequestType type)
            {
                return Sessions.ValidateRequestAsync(new RequestHeader
                {
                    AuthenticationToken = Created.AuthenticationToken,
                    Timestamp = Clock.GetUtcNow().UtcDateTime
                }, m_channel, type, RequestLifetime.None);
            }

            /// <summary>
            /// Executes the real Read dispatcher and its node authorization before touching the value.
            /// </summary>
            public async Task<DataValue> ReadAsync()
            {
                using OperationContext context = await AdmitAsync(RequestType.Read).ConfigureAwait(false);
                (ArrayOf<DataValue> values, _) = await m_dispatcher.ReadAsync(context, 0, TimestampsToReturn.Both,
                    [new ReadValueId { NodeId = m_value.NodeId, AttributeId = Attributes.Value }])
                    .ConfigureAwait(false);
                return values[0];
            }

            /// <summary>
            /// Creates an ordinary subscription and real queue-backed monitored item through both service layers.
            /// </summary>
            public async Task<Subscription> CreateValueSubscriptionAsync()
            {
                using OperationContext context = await AdmitAsync(RequestType.CreateSubscription).ConfigureAwait(false);
                CreateSubscriptionResponse created = await m_subscriptions.CreateSubscriptionAsync(
                    context, 100, 30, 10, 1, true, 0).ConfigureAwait(false);
                Assert.That(m_subscriptions.TryGetSubscription(created.SubscriptionId, out ISubscription? subscription),
                    Is.True);
                var concrete = (Subscription)subscription!;
                CreateMonitoredItemsResponse items = await concrete.CreateMonitoredItemsAsync(
                    context, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId { NodeId = m_value.NodeId, AttributeId = Attributes.Value },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1, SamplingInterval = 0, QueueSize = 10, DiscardOldest = true
                            }
                        }
                    ]).ConfigureAwait(false);
                Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                return concrete;
            }

            /// <summary>
            /// Creates a real filtered event monitored item on an ordinary notifier owned by this manager.
            /// </summary>
            public async Task<Subscription> CreateEventSubscriptionAsync(bool restricted, bool namespacePolicy = false)
            {
                m_notifier = new BaseObjectState(null)
                {
                    NodeId = new NodeId("EventNotifier", m_owner.NamespaceIndex),
                    BrowseName = new QualifiedName("EventNotifier", m_owner.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents,
                    AccessRestrictions = restricted
                        ? AccessRestrictionType.EncryptionRequired : AccessRestrictionType.None,
                    RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = ObjectIds.WellKnownRole_Observer,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.ReceiveEvents)
                        }
                    ]
                };
                if (namespacePolicy)
                {
                    var metadata = new NamespaceMetadataState(null);
                    metadata.AddDefaultAccessRestrictions(m_owner.SystemContext);
                    metadata.DefaultAccessRestrictions!.Value = (ushort)(restricted
                        ? AccessRestrictionType.EncryptionRequired : AccessRestrictionType.None);
                    DeterministicServerMock.SetNamespaceMetadata(m_server, m_owner.NamespaceIndex, metadata);
                    m_notifier.AccessRestrictions = null;
                }
                await m_owner.AddNodeAsync(m_owner.SystemContext, NodeId.Null, m_notifier).ConfigureAwait(false);
                m_eventType = new BaseObjectTypeState
                {
                    NodeId = new NodeId("TransferEventType", m_owner.NamespaceIndex),
                    BrowseName = new QualifiedName("TransferEventType", m_owner.NamespaceIndex),
                    SuperTypeId = ObjectTypeIds.BaseEventType,
                    RolePermissions = m_notifier.RolePermissions
                };
                await m_owner.AddPredefinedNodePublicAsync(m_owner.SystemContext, m_eventType).ConfigureAwait(false);
                using OperationContext context = await AdmitAsync(RequestType.CreateSubscription).ConfigureAwait(false);
                CreateSubscriptionResponse created = await m_subscriptions.CreateSubscriptionAsync(
                    context, 100, 30, 10, 1, true, 0).ConfigureAwait(false);
                Assert.That(m_subscriptions.TryGetSubscription(created.SubscriptionId, out ISubscription? subscription),
                    Is.True);
                var concrete = (Subscription)subscription!;
                CreateMonitoredItemsResponse items = await concrete.CreateMonitoredItemsAsync(
                    context, TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = m_notifier.NodeId, AttributeId = Attributes.EventNotifier
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1, QueueSize = 10, DiscardOldest = true,
                                Filter = new ExtensionObject(new EventFilter
                                {
                                    SelectClauses =
                                    [
                                        new SimpleAttributeOperand
                                        {
                                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                            BrowsePath = [new QualifiedName(BrowseNames.Message)],
                                            AttributeId = Attributes.Value
                                        }
                                    ]
                                })
                            }
                        }
                    ]).ConfigureAwait(false);
                Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                return concrete;
            }

            /// <summary>
            /// Creates and authenticates a second independent session, with a real RSA proof on a Sign channel.
            /// </summary>
            public async Task<(CreateSessionResult Created, SecureChannelContext Channel)> CreateDestinationAsync(
                bool encrypted, bool sameChannel = false)
            {
                string policyUri = encrypted ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256;
                var endpoint = sameChannel
                    ? m_channel.EndpointDescription!
                    : new EndpointDescription
                {
                    EndpointUrl = encrypted
                        ? "https://localhost/event-destination" : "opc.tcp://localhost/event-destination",
                    TransportProfileUri = encrypted ? Profiles.HttpsBinaryTransport : Profiles.UaTcpTransport,
                    SecurityMode = encrypted ? MessageSecurityMode.None : MessageSecurityMode.Sign,
                    SecurityPolicyUri = policyUri,
                    ServerCertificate = m_certificate.RawData.ToByteString(),
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "username", TokenType = UserTokenType.UserName, SecurityPolicyUri = policyUri
                        }
                    ]
                };
                SecureChannelContext channel = sameChannel
                    ? m_channel
                    : new SecureChannelContext("independent-destination", endpoint, RequestEncoding.Binary,
                        m_destinationCertificate.RawData);
                using var context = new OperationContext(new RequestHeader(), channel,
                    RequestType.ActivateSession, RequestLifetime.None);
                ByteString clientNonce = Nonce.CreateRandomNonceData(32).ToByteString();
                CreateSessionResult created = await Sessions.CreateSessionAsync(context, m_certificate,
                    "destination", clientNonce, new ApplicationDescription(), endpoint.EndpointUrl,
                    m_destinationCertificate.AddRef(), [], 60_000, 65_536).ConfigureAwait(false);
                ByteString serverNonce = ((Opc.Ua.Server.Session)created.Session).GetServerNonceData();
                var handler = new UserNameIdentityTokenHandler("alice", [1]);
                handler.UpdatePolicy(endpoint.UserIdentityTokens[0]);
                await handler.EncryptAsync(m_certificate, serverNonce.ToArray(), policyUri,
                    m_server.Object.MessageContext).ConfigureAwait(false);
                SignatureData signature = new();
                if (!sameChannel && !encrypted)
                {
                    SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(policyUri)!;
                    byte[] signed = policy.GetClientSignatureData(null, serverNonce.ToArray(),
                        m_certificate.RawData, null, null, clientNonce.ToArray());
                    signature = await SecurityPolicies.Default.CreateSignatureDataAsync(
                        policy, m_destinationCertificate, signed, CancellationToken.None).ConfigureAwait(false);
                }
                await Sessions.ActivateSessionAsync(context, created.AuthenticationToken, signature,
                    new ExtensionObject(handler.Token), new SignatureData(), []).ConfigureAwait(false);
                return (created, channel);
            }

            /// <summary>
            /// Admits a request from an independent session using that session's physical channel.
            /// </summary>
            public ValueTask<OperationContext> AdmitAsync(
                CreateSessionResult created, SecureChannelContext channel, RequestType type)
            {
                return Sessions.ValidateRequestAsync(new RequestHeader
                {
                    AuthenticationToken = created.AuthenticationToken,
                    Timestamp = Clock.GetUtcNow().UtcDateTime
                }, channel, type, RequestLifetime.None);
            }

            /// <summary>
            /// Executes the real subscription transfer service.
            /// </summary>
            public ValueTask<TransferSubscriptionsResponse> TransferAsync(OperationContext context, uint id)
            {
                return m_subscriptions.TransferSubscriptionsAsync(context, [id], false);
            }

            /// <summary>
            /// Tightens only the notifier's access restrictions, leaving session roles and event identities unchanged.
            /// </summary>
            public void RequireNotifierEncryption(bool namespacePolicy = false)
            {
                if (namespacePolicy)
                {
                    var metadata = new NamespaceMetadataState(null);
                    metadata.AddDefaultAccessRestrictions(m_owner.SystemContext);
                    metadata.DefaultAccessRestrictions!.Value = (ushort)AccessRestrictionType.EncryptionRequired;
                    DeterministicServerMock.SetNamespaceMetadata(m_server, m_owner.NamespaceIndex, metadata);
                    m_notifier!.AccessRestrictions = null;
                }
                else
                {
                    m_notifier!.AccessRestrictions = AccessRestrictionType.EncryptionRequired;
                }
            }

            /// <summary>
            /// Reports and drains an ordinary event through the production notifier.
            /// </summary>
            public async Task ReportEventAsync(string message)
            {
                var evt = new BaseEventState(null);
                evt.Initialize(m_owner.SystemContext, m_notifier, EventSeverity.Low, new LocalizedText(message));
                evt.EventType!.Value = m_eventType!.NodeId;
                evt.TypeDefinitionId = m_eventType.NodeId;
                await m_notifier!.ReportEventAsync(m_owner.SystemContext, evt).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await m_owner.MonitoredNodes[m_notifier.NodeId].WaitForPendingNotificationsAsync(timeout.Token)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Publishes a provider value and deterministically drains background notification authorization.
            /// </summary>
            public async Task QueueValueAsync(int value)
            {
                m_value.Value = value;
                await m_value.ClearChangeMasksAsync(m_owner.SystemContext, false).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await m_owner.MonitoredNodes[m_value.NodeId].WaitForPendingNotificationsAsync(timeout.Token)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Disposes retained messages before closing sessions and their node services.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                m_subscriptions.Dispose();
                m_events.Dispose();
                foreach (ISession session in Sessions.GetSessions())
                {
                    await Sessions.CloseSessionAsync(session.Id).ConfigureAwait(false);
                }
                Sessions.Dispose();
                await m_dispatcher.DisposeAsync().ConfigureAwait(false);
                m_roles.Dispose();
                m_certificate.Dispose();
                m_destinationCertificate.Dispose();
                m_queues.Dispose();
                m_resources.Dispose();
            }

            /// <summary>The server services used by the real managers.</summary>
            private readonly Mock<IServerInternal> m_server;

            /// <summary>The event field localization service.</summary>
            private readonly ResourceManager m_resources;
            /// <summary>The real role configuration owner.</summary>
            private readonly RoleManager m_roles = new();
            /// <summary>The real value owner.</summary>
            // The dispatcher owns this registration and awaits its disposal. TODO: model transitive ownership in CA2213.
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
                Justification = "MasterNodeManager.DisposeAsync disposes its registered node managers.")]
            private readonly TestableAsyncCustomNodeManager m_owner;
            /// <summary>The real protected value.</summary>
            private readonly BaseDataVariableState m_value;
            /// <summary>The service dispatcher.</summary>
            private readonly MasterNodeManager m_dispatcher;
            /// <summary>The subscription and retransmission owner.</summary>
            private readonly SubscriptionManager m_subscriptions;
            /// <summary>The application certificate owned by this fixture.</summary>
            private readonly Certificate m_certificate;
            /// <summary>The confidential same-channel transport evidence.</summary>
            private readonly SecureChannelContext m_channel;
            /// <summary>The queue factory.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
            /// <summary>The production event monitored-item factory and owner.</summary>
            private readonly EventManager m_events;
            /// <summary>The ordinary notifier protected independently of event source/type role ACLs.</summary>
            private BaseObjectState? m_notifier;
            /// <summary>The registered ordinary event type used for ReceiveEvents permission evaluation.</summary>
            private BaseObjectTypeState? m_eventType;
            /// <summary>The original application key presented on the independent Sign channel.</summary>
            private readonly Certificate m_destinationCertificate =
                CertificateBuilder.Create("CN=Event Transfer Destination").SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Exposes a deterministic boundary after real role resolution and before activation identity installation.
        /// </summary>
        private sealed class RoleRaceSessionManager : SessionManager
        {
            /// <summary>
            /// Creates the real session manager.
            /// </summary>
            public RoleRaceSessionManager(
                IServerInternal server, ApplicationConfiguration configuration, TimeProvider clock)
                : base(server, configuration, clock)
            {
            }

            /// <summary>
            /// Gets or sets the one-shot mutation performed after resolving the activation's roles.
            /// </summary>
            public Action? AfterResolution { get; set; }

            /// <inheritdoc/>
            protected override IUserIdentity AddMandatoryRoles(
                ISession session, OperationContext context, IUserIdentity effectiveIdentity)
            {
                IUserIdentity resolved = base.AddMandatoryRoles(session, context, effectiveIdentity);
                Action? mutation = AfterResolution;
                AfterResolution = null;
                mutation?.Invoke();
                return resolved;
            }
        }
    }
}
