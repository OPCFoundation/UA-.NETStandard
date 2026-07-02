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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="SharedKeyValueSubscriptionStore"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Subscription")]
    [Parallelizable(ParallelScope.All)]
    public class SharedKeyValueSubscriptionStoreTests
    {
        private const ushort NamespaceIndex = 2;

        [Test]
        public async Task StoreAndRestoreRoundTripsDefinitionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            StoredSubscription expected = NewSubscription(100, 10);

            bool stored = await store.StoreSubscriptionsAsync([expected]).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(stored, Is.True);
            Assert.That(result.Success, Is.True);
            var actual = (StoredSubscription)result.Subscriptions!.Single();
            AssertSubscription(actual, expected);
            AssertMonitoredItem((StoredMonitoredItem)actual.MonitoredItems.Single(), NewItem(100, 10));
        }

        [Test]
        public async Task StoreIsVisibleToSecondReplicaAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore active = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);
            StoredSubscription expected = NewSubscription(200, 20);

            await active.StoreSubscriptionsAsync([expected]).ConfigureAwait(false);
            RestoreSubscriptionResult result = await backup.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            var actual = (StoredSubscription)result.Subscriptions!.Single();
            AssertSubscription(actual, expected);
        }

        [Test]
        public async Task ProtectedDefinitionCacheSurvivesTamperedBackendRecordAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(11));
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            StoredSubscription subscription = NewSubscription(300, 30);
            await store.StoreSubscriptionsAsync([subscription]).ConfigureAwait(false);

            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.KeyFor(subscription.Id),
                ByteString.From(new byte[] { 1, 2, 3, 4, 5 })).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Subscriptions, Is.Not.Empty);
        }

        [Test]
        public async Task ProtectedStoreDoesNotPersistDefinitionInClearTextAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(12));
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            StoredSubscription subscription = NewSubscription(400, 40);

            await store.StoreSubscriptionsAsync([subscription]).ConfigureAwait(false);
            (bool found, ByteString raw) = await kv.TryGetAsync(SharedKeyValueSubscriptionStore.KeyFor(subscription.Id)).ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(Contains(raw.ToArray(), BitConverter.GetBytes(subscription.Id)), Is.False);
            Assert.That((await store.RestoreSubscriptionsAsync().ConfigureAwait(false)).Subscriptions!.Single().Id, Is.EqualTo(subscription.Id));
        }

        [Test]
        public async Task StoreReplacesRemovedSubscriptionsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            await store.StoreSubscriptionsAsync([NewSubscription(500, 50), NewSubscription(501, 51)]).ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([NewSubscription(501, 51)]).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Subscriptions!.Select(s => s.Id), Is.EqualTo(new uint[] { 501 }));
        }

        [Test]
        public async Task OnSubscriptionRestoreCompleteCleansStaleDefinitionsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            await store.StoreSubscriptionsAsync([NewSubscription(600, 60), NewSubscription(601, 61)]).ConfigureAwait(false);

            await store.OnSubscriptionRestoreCompleteAsync(new Dictionary<uint, ArrayOf<uint>>
            {
                [601] = new ArrayOf<uint>(new uint[] { 61 })
            }).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Subscriptions!.Select(s => s.Id), Is.EqualTo(new uint[] { 601 }));
        }

        [Test]
        public async Task RetransmissionStateIsVisibleToSecondReplicaAndKeepsSequenceContinuityAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore primary = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);

            primary.StoreRetransmissionState(
                700,
                4,
                [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await primary.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(700).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(4));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1, 2, 3 }));

            NotificationMessage republished = state.SentMessages.Memory.ToArray().Single(m => m.SequenceNumber == 2);
            Assert.That(republished.NotificationData, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RetransmissionAcknowledgeEvictsMirroredNotificationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore primary = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);
            primary.StoreRetransmissionState(
                701,
                4,
                [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await primary.FlushAsync().ConfigureAwait(false);

            primary.AcknowledgeNotification(701, 2);
            await primary.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(701).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(
                state!.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1, 3 }));
        }

        [Test]
        public async Task RetransmissionSnapshotRemovesStaleSequenceKeysAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            store.StoreRetransmissionState(
                702,
                4,
                [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await store.FlushAsync().ConfigureAwait(false);

            store.StoreRetransmissionState(702, 5, [NewNotification(3), NewNotification(4)]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(702).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(5));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 3, 4 }));
        }

        [Test]
        public async Task RetransmissionDeltaAddsAndRemovesOnlyChangedMessagesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);

            store.StoreRetransmissionStateDelta(708, 4, [NewNotification(1), NewNotification(2)], []);
            await store.FlushAsync().ConfigureAwait(false);
            store.StoreRetransmissionStateDelta(708, 5, [NewNotification(3)], [1]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(708).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(5));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 2, 3 }));
        }

        [Test]
        public async Task RetransmissionMessagesDoNotRepeatNamespaceTablesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            store.StoreRetransmissionStateDelta(709, 2, [NewNotification(1)], []);
            await store.FlushAsync().ConfigureAwait(false);
            (bool stateFound, ByteString stateRaw) = await kv.TryGetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(709)).ConfigureAwait(false);
            (bool messageFound, ByteString messageRaw) = await kv.TryGetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionMessageKeyFor(709, 1)).ConfigureAwait(false);

            byte[] namespaceBytes = Encoding.UTF8.GetBytes("urn:test:subscriptions");
            Assert.That(stateFound, Is.True);
            Assert.That(messageFound, Is.True);
            Assert.That(Contains(stateRaw.ToArray(), namespaceBytes), Is.True);
            Assert.That(Contains(messageRaw.ToArray(), namespaceBytes), Is.False);
        }

        [Test]
        public async Task RetransmissionTamperFailsClosedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(13));
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            store.StoreRetransmissionState(703, 2, [NewNotification(1)]);
            await store.FlushAsync().ConfigureAwait(false);

            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(703),
                ByteString.From(new byte[] { 9, 8, 7 })).ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(703).ConfigureAwait(false);

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task MissingRetransmissionStateReturnsNullAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(704).ConfigureAwait(false);

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task RetransmissionMirrorUsesAsyncBackendWithoutBlockingAsync()
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new AsyncSharedKeyValueStore(inner);
            var primary = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            var backup = new SharedKeyValueSubscriptionStore(kv, CreateContext());

            primary.StoreRetransmissionState(705, 3, [NewNotification(1), NewNotification(2)]);
            await primary.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(705).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(3));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public async Task RetransmissionDeleteIsOrderedAfterInflightMirrorWritesAsync()
        {
            const uint subscriptionId = 706;
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new BlockingRetransmissionSetStore(inner, subscriptionId);
            await using var store = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            await store.StoreSubscriptionsAsync([NewSubscription(subscriptionId, 76)]).ConfigureAwait(false);

            store.StoreRetransmissionState(subscriptionId, 3, [NewNotification(1), NewNotification(2)]);
            await kv.WaitForBlockedSetAsync().ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([]).ConfigureAwait(false);
            kv.ReleaseBlockedSets();
            await store.FlushAsync().ConfigureAwait(false);

            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(state, Is.Null);
            Assert.That(await CountKeysAsync(inner, RetransmissionPrefixFor(subscriptionId)).ConfigureAwait(false), Is.Zero);
        }

        [Test]
        public async Task ReusedSubscriptionIdDoesNotLoadDeletedRetransmissionStateAsync()
        {
            const uint subscriptionId = 707;
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            await using SharedKeyValueSubscriptionStore backup = CreateStore(kv);

            await store.StoreSubscriptionsAsync([NewSubscription(subscriptionId, 77)]).ConfigureAwait(false);
            store.StoreRetransmissionState(subscriptionId, 4, [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await store.FlushAsync().ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([]).ConfigureAwait(false);
            await store.FlushAsync().ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([NewSubscription(subscriptionId, 78)]).ConfigureAwait(false);

            SubscriptionRetransmissionState? deletedState = await backup.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);
            store.StoreRetransmissionState(subscriptionId, 10, [NewNotification(9)]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? newState = await backup.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(deletedState, Is.Null);
            Assert.That(newState, Is.Not.Null);
            Assert.That(newState!.NextSequenceNumber, Is.EqualTo(10));
            Assert.That(
                newState.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 9 }));
        }

        [Test]
        public void ConstructorValidatesArguments()
        {
            IServiceMessageContext context = CreateContext();
            var kvMock = new Mock<ISharedKeyValueStore>();

            Assert.That(
                () => new SharedKeyValueSubscriptionStore(null!, context),
                Throws.ArgumentNullException);
            Assert.That(
                () => new SharedKeyValueSubscriptionStore(kvMock.Object, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RestoreQueueFallbacksReturnNull()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(store.RestoreDataChangeMonitoredItemQueue(1), Is.Null);
            Assert.That(store.RestoreEventMonitoredItemQueue(1), Is.Null);
        }

        [Test]
        public void OnSubscriptionRestoreCompleteValidatesArguments()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(
                async () => await store.OnSubscriptionRestoreCompleteAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DefinitionCodecRoundTripsAllSupportedFilterShapes()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            StoredSubscription subscription = NewSubscription(900, 90);
            subscription.MonitoredItems =
            [
                NewItemWithFilter(900, 90, null),
                NewItemWithFilter(900, 91, new EventFilter()),
                NewItemWithFilter(900, 92, new AggregateFilter
                {
                    AggregateType = ObjectIds.AggregateFunction_Average,
                    ProcessingInterval = 1000,
                    StartTime = new DateTimeUtc(638000000000000000)
                })
            ];

            MethodInfo encode = GetPrivateMethod("Encode", typeof(StoredSubscription));
            MethodInfo decode = GetPrivateMethod("Decode", typeof(ByteString));
            var payload = (ByteString)encode.Invoke(store, [subscription])!;
            var decoded = (StoredSubscription)decode.Invoke(store, [payload])!;
            var decodedItems = decoded.MonitoredItems.ToList();

            Assert.That(decodedItems, Has.Count.EqualTo(3));
            Assert.That(decodedItems[0].FilterToUse, Is.Null);
            Assert.That(decodedItems[1].FilterToUse, Is.TypeOf<EventFilter>());
            Assert.That(decodedItems[2].FilterToUse, Is.TypeOf<AggregateFilter>());
        }

        [Test]
        public void DefinitionDecodeRejectsUnsupportedVersion()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 999);
            var payload = ByteString.From(encoder.CloseAndReturnBuffer());
            MethodInfo decode = GetPrivateMethod("Decode", typeof(ByteString));

            TargetInvocationException? ex = Assert.Throws<TargetInvocationException>(
                () => decode.Invoke(store, [payload]));

            Assert.That(ex!.InnerException, Is.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task ContinuationPointLoadIgnoresUnsupportedEnvelopeVersionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var continuationPointId = Guid.NewGuid();
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 999);
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.ContinuationPointKeyFor(
                    sessionId,
                    ContinuationPointKind.Browse,
                    continuationPointId),
                ByteString.From(encoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(sessionId).ConfigureAwait(false);

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public async Task ContinuationPointLoadIgnoresInvalidEnvelopeIdAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var continuationPointId = Guid.NewGuid();
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 1);
            encoder.WriteByteString(null, ByteString.From(new byte[] { 1, 2, 3 }));
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.ContinuationPointKeyFor(
                    sessionId,
                    ContinuationPointKind.Browse,
                    continuationPointId),
                ByteString.From(encoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(sessionId).ConfigureAwait(false);

            Assert.That(envelopes, Is.Empty);
        }

        private static SharedKeyValueSubscriptionStore CreateStore(InMemorySharedKeyValueStore kv)
        {
            return new SharedKeyValueSubscriptionStore(kv, CreateContext());
        }

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:test:subscriptions");
            return context;
        }

        private static StoredSubscription NewSubscription(uint subscriptionId, uint monitoredItemId)
        {
            return new StoredSubscription
            {
                Id = subscriptionId,
                IsDurable = true,
                LifetimeCounter = 2,
                MaxLifetimeCount = 120,
                MaxKeepaliveCount = 12,
                MaxMessageCount = 8,
                MaxNotificationsPerPublish = 99,
                PublishingInterval = 250,
                Priority = 7,
                LastSentMessage = 0,
                SequenceNumber = 0,
                SentMessages = [],
                UserIdentityToken = new AnonymousIdentityToken(),
                MonitoredItems = [NewItem(subscriptionId, monitoredItemId)]
            };
        }

        private static StoredMonitoredItem NewItem(uint subscriptionId, uint monitoredItemId)
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.5
            };

            return new StoredMonitoredItem
            {
                IsRestored = false,
                AlwaysReportUpdates = true,
                AttributeId = Attributes.Value,
                ClientHandle = 987,
                DiagnosticsMasks = DiagnosticsMasks.OperationAll,
                DiscardOldest = true,
                Encoding = new QualifiedName(BrowseNames.DefaultXml, 0),
                Id = monitoredItemId,
                IndexRange = "1:3",
                ParsedIndexRange = NumericRange.Parse("1:3"),
                IsDurable = true,
                LastError = ServiceResult.Good,
                LastValue = new DataValue(new Variant(42)),
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId("Temperature", NamespaceIndex),
                FilterToUse = filter,
                OriginalFilter = filter,
                QueueSize = 3,
                Range = 100,
                SamplingInterval = 50,
                SourceSamplingInterval = 25,
                SubscriptionId = subscriptionId,
                TimestampsToReturn = TimestampsToReturn.Both,
                TypeMask = 1
            };
        }

        private static StoredMonitoredItem NewItemWithFilter(
            uint subscriptionId,
            uint monitoredItemId,
            MonitoringFilter? filter)
        {
            StoredMonitoredItem item = NewItem(subscriptionId, monitoredItemId);
            item.FilterToUse = filter!;
            item.OriginalFilter = filter!;
            return item;
        }

        private static MethodInfo GetPrivateMethod(string name, params Type[] parameterTypes)
        {
            MethodInfo? method = typeof(SharedKeyValueSubscriptionStore).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);
            Assert.That(method, Is.Not.Null);
            return method!;
        }

        private static NotificationMessage NewNotification(uint sequenceNumber)
        {
            var notification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = sequenceNumber,
                        Value = new DataValue(new Variant((int)sequenceNumber))
                    }
                ]
            };

            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                PublishTime = DateTimeUtc.Now,
                NotificationData = [new ExtensionObject(notification)]
            };
        }

        private static string RetransmissionPrefixFor(uint subscriptionId)
        {
            string messageKey = SharedKeyValueSubscriptionStore.RetransmissionMessageKeyFor(subscriptionId, 0);
            return messageKey[..^10];
        }

        private static async Task<int> CountKeysAsync(InMemorySharedKeyValueStore store, string keyPrefix)
        {
            int count = 0;
            await foreach (KeyValuePair<string, ByteString> pair in store.ScanAsync(keyPrefix))
            {
                _ = pair;
                count++;
            }
            (bool found, _) = await store.TryGetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(
                    uint.Parse(keyPrefix.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture))).ConfigureAwait(false);
            return found ? count + 1 : count;
        }

        private static void AssertSubscription(StoredSubscription actual, StoredSubscription expected)
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.IsDurable, Is.EqualTo(expected.IsDurable));
            Assert.That(actual.MaxLifetimeCount, Is.EqualTo(expected.MaxLifetimeCount));
            Assert.That(actual.MaxKeepaliveCount, Is.EqualTo(expected.MaxKeepaliveCount));
            Assert.That(actual.MaxNotificationsPerPublish, Is.EqualTo(expected.MaxNotificationsPerPublish));
            Assert.That(actual.PublishingInterval, Is.EqualTo(expected.PublishingInterval));
            Assert.That(actual.Priority, Is.EqualTo(expected.Priority));
        }

        private static void AssertMonitoredItem(StoredMonitoredItem actual, StoredMonitoredItem expected)
        {
            Assert.That(actual.SubscriptionId, Is.EqualTo(expected.SubscriptionId));
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.NodeId, Is.EqualTo(expected.NodeId));
            Assert.That(actual.AttributeId, Is.EqualTo(expected.AttributeId));
            Assert.That(actual.MonitoringMode, Is.EqualTo(expected.MonitoringMode));
            Assert.That(actual.SamplingInterval, Is.EqualTo(expected.SamplingInterval));
            Assert.That(actual.QueueSize, Is.EqualTo(expected.QueueSize));
            Assert.That(actual.DiscardOldest, Is.EqualTo(expected.DiscardOldest));
            Assert.That(actual.FilterToUse, Is.TypeOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)actual.FilterToUse).DeadbandValue, Is.EqualTo(1.5));
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int ii = 0; ii + needle.Length <= haystack.Length; ii++)
            {
                bool match = true;
                for (int jj = 0; jj < needle.Length; jj++)
                {
                    if (haystack[ii + jj] != needle[jj])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return true;
                }
            }
            return false;
        }

        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int ii = 0; ii < key.Length; ii++)
            {
                key[ii] = (byte)(seed + ii);
            }
            return key;
        }

        private sealed class AsyncSharedKeyValueStore : ISharedKeyValueStore
        {
            public AsyncSharedKeyValueStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                await Task.Yield();
                return await m_inner.TryGetAsync(key, ct).ConfigureAwait(false);
            }

            public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                await Task.Yield();
                await m_inner.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            public async ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                await Task.Yield();
                return await m_inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            }

            public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                await Task.Yield();
                return await m_inner.DeleteAsync(key, ct).ConfigureAwait(false);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                await foreach (KeyValuePair<string, ByteString> pair in m_inner.ScanAsync(keyPrefix, ct))
                {
                    yield return pair;
                }
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                await foreach (KeyValueChange change in m_inner.WatchAsync(keyPrefix, ct))
                {
                    yield return change;
                }
            }

            private readonly ISharedKeyValueStore m_inner;
        }

        private sealed class BlockingRetransmissionSetStore : ISharedKeyValueStore
        {
            public BlockingRetransmissionSetStore(ISharedKeyValueStore inner, uint subscriptionId)
            {
                m_inner = inner;
                m_keyPrefix = "subscription-retransmission/" +
                    subscriptionId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) +
                    "/";
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                if (key.StartsWith(m_keyPrefix, StringComparison.Ordinal) &&
                    Interlocked.Exchange(ref m_blockedSetCount, 1) == 0)
                {
                    m_blocked.SetResult(true);
                    await m_release.Task.ConfigureAwait(false);
                }

                await m_inner.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await foreach (KeyValuePair<string, ByteString> pair in m_inner.ScanAsync(keyPrefix, ct))
                {
                    yield return pair;
                }
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await foreach (KeyValueChange change in m_inner.WatchAsync(keyPrefix, ct))
                {
                    yield return change;
                }
            }

            public async Task WaitForBlockedSetAsync()
            {
                Task completed = await Task.WhenAny(m_blocked.Task, Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(m_blocked.Task));
            }

            public void ReleaseBlockedSets()
            {
                m_release.SetResult(true);
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly string m_keyPrefix;

            private readonly TaskCompletionSource<bool> m_blocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_blockedSetCount;
        }
    }
}
