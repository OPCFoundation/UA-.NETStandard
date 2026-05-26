/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * MIT License - see /Docs/License.md
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Tests.Alarms
{
    /// <summary>
    /// Tests for <see cref="AlarmStreamExtensions"/>: the
    /// <c>SubscribeAlarmsAsync</c>, <c>SubscribeConditionsAsync</c>,
    /// and <c>SubscribeDialogsAsync</c> helpers that wrap an
    /// <see cref="IStreamingSubscription"/> and decode raw events into
    /// typed records.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Alarms")]
    [Parallelizable]
    public sealed class AlarmStreamExtensionsTests
    {
        private static readonly NodeId s_notifier = ObjectIds.Server;

        [Test]
        public void SubscribeAlarmsAsyncWithNullSubscriptionThrowsArgumentNullException()
        {
            Assert.That(
                () => AlarmStreamExtensions.SubscribeAlarmsAsync(null!, s_notifier),
                Throws.ArgumentNullException);
        }

        [Test]
        public void SubscribeConditionsAsyncWithNullSubscriptionThrowsArgumentNullException()
        {
            Assert.That(
                () => AlarmStreamExtensions.SubscribeConditionsAsync(null!, s_notifier),
                Throws.ArgumentNullException);
        }

        [Test]
        public void SubscribeDialogsAsyncWithNullSubscriptionThrowsArgumentNullException()
        {
            Assert.That(
                () => AlarmStreamExtensions.SubscribeDialogsAsync(null!, s_notifier),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SubscribeAlarmsAsyncUsesForAlarmsFilterWhenNoBuilderProvided()
        {
            var fake = new FakeStreamingSubscription();

            // Drain the enumerable so the fake observes the filter.
            await foreach (ConditionTypeRecord _ in fake
                .SubscribeAlarmsAsync(s_notifier).ConfigureAwait(false))
            {
            }

            Assert.That(fake.LastFilter, Is.Not.Null);
            Assert.That(fake.LastFilter!.SelectClauses.Count,
                Is.EqualTo(AlarmEventDecoder.StandardFields.Length));
            NodeId target = GetOfTypeNodeId(fake.LastFilter);
            Assert.That(target, Is.EqualTo(ObjectTypeIds.AlarmConditionType));
        }

        [Test]
        public async Task SubscribeAlarmsAsyncHonorsCustomFilterBuilder()
        {
            var fake = new FakeStreamingSubscription();
            var builder = new AlarmEventFilterBuilder().ForConditions();

            await foreach (ConditionTypeRecord _ in fake
                .SubscribeAlarmsAsync(s_notifier, builder).ConfigureAwait(false))
            {
            }

            NodeId target = GetOfTypeNodeId(fake.LastFilter!);
            Assert.That(target, Is.EqualTo(ObjectTypeIds.ConditionType));
        }

        [Test]
        public async Task SubscribeConditionsAsyncUsesForConditionsFilter()
        {
            var fake = new FakeStreamingSubscription();

            await foreach (ConditionTypeRecord _ in fake
                .SubscribeConditionsAsync(s_notifier).ConfigureAwait(false))
            {
            }

            NodeId target = GetOfTypeNodeId(fake.LastFilter!);
            Assert.That(target, Is.EqualTo(ObjectTypeIds.ConditionType));
        }

        [Test]
        public async Task SubscribeAlarmsAsyncYieldsDecodedRecordsAndDropsNullDecodes()
        {
            var fake = new FakeStreamingSubscription
            {
                Events =
                [
                    new EventNotification(null, default),                       // decodes to null
                    new EventNotification(null, ArrayOf.Wrapped(MakeConditionFields()))
                ]
            };

            var collected = new List<ConditionTypeRecord>();
            await foreach (ConditionTypeRecord record in fake
                .SubscribeAlarmsAsync(s_notifier).ConfigureAwait(false))
            {
                collected.Add(record);
            }

            Assert.That(collected, Has.Count.EqualTo(1));
            Assert.That(collected[0].ConditionName, Is.EqualTo("MyCondition"));
        }

        [Test]
        public async Task SubscribeDialogsAsyncYieldsOnlyDialogConditionTypeRecord()
        {
            var fake = new FakeStreamingSubscription
            {
                Events =
                [
                    new EventNotification(null, ArrayOf.Wrapped(MakeConditionFields())),
                    new EventNotification(null, ArrayOf.Wrapped(MakeDialogFields()))
                ]
            };

            var collected = new List<DialogConditionTypeRecord>();
            await foreach (DialogConditionTypeRecord record in fake
                .SubscribeDialogsAsync(s_notifier).ConfigureAwait(false))
            {
                collected.Add(record);
            }

            Assert.That(collected, Has.Count.EqualTo(1));
        }

        [Test]
        public void SubscribeAlarmsAsyncPropagatesCancellation()
        {
            using var cts = new CancellationTokenSource();
            var fake = new FakeStreamingSubscription { ThrowOnCancel = true };
            cts.Cancel();

            Assert.That(async () =>
            {
                await foreach (ConditionTypeRecord _ in fake
                    .SubscribeAlarmsAsync(s_notifier, filterBuilder: null, options: null, cts.Token)
                    .ConfigureAwait(false))
                {
                }
            }, Throws.InstanceOf<OperationCanceledException>());
        }

        private static Variant[] MakeConditionFields()
        {
            // 15-field condition payload — index 11 (EnabledState.Id) is
            // a bool, which triggers AlarmEventDecoder to return a
            // ConditionTypeRecord. The string at index 8 lets us assert
            // we received the populated record below.
            return
            [
                Variant.From(new ByteString(new byte[] { 1 })),
                Variant.From((NodeId)ObjectTypeIds.ConditionType),
                Variant.From((NodeId)new NodeId(1u, 0)),
                Variant.From("Src"),
                Variant.From(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Variant.From(new DateTime(2024, 1, 1, 0, 0, 1, DateTimeKind.Utc)),
                Variant.From(new LocalizedText("en", "msg")),
                Variant.From((ushort)100),
                Variant.From("MyCondition"),
                default,
                Variant.From(false),
                Variant.From(true),
                Variant.From(new StatusCode(0)),
                Variant.From(new LocalizedText("en", string.Empty)),
                Variant.From(string.Empty)
            ];
        }

        private static Variant[] MakeDialogFields()
        {
            // Dialog records require a populated DialogState.Id at
            // index 24 (see AlarmEventDecoder.StandardFields).
            var fields = new List<Variant>(MakeConditionFields());
            while (fields.Count < 24)
            {
                fields.Add(default);
            }
            fields.Add(Variant.From(true));    // 24: DialogState.Id
            return fields.ToArray();
        }

        private static NodeId GetOfTypeNodeId(EventFilter filter)
        {
            Assert.That(filter.WhereClause.Elements.Count, Is.EqualTo(1));
            ContentFilterElement element = filter.WhereClause.Elements[0];
            Assert.That(element.FilterOperator, Is.EqualTo(FilterOperator.OfType));
            Assert.That(element.FilterOperands[0]
                .TryGetValue(out LiteralOperand? literal), Is.True);
            Assert.That(literal!.Value.TryGetValue(out NodeId nodeId), Is.True);
            return nodeId;
        }

        /// <summary>
        /// Fake <see cref="IStreamingSubscription"/> driving deterministic
        /// <see cref="EventNotification"/> sequences and capturing the
        /// filter / options / cancellation arguments the production code
        /// passes through.
        /// </summary>
        private sealed class FakeStreamingSubscription : IStreamingSubscription
        {
            public List<EventNotification> Events { get; init; } = [];

            public EventFilter? LastFilter { get; private set; }

            public bool ThrowOnCancel { get; init; }

            public async IAsyncEnumerable<EventNotification> SubscribeEventsAsync(
                NodeId notifierId,
                EventFilter filter,
                MItemOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                LastFilter = filter;
                foreach (EventNotification n in Events)
                {
                    if (ThrowOnCancel)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    yield return n;
                    await Task.Yield();
                }
                if (ThrowOnCancel)
                {
                    ct.ThrowIfCancellationRequested();
                }
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                NodeId nodeId,
                MItemOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds,
                MItemOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
