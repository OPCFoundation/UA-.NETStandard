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
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

// Local fake IStreamingSubscription test types are no-op IAsyncDisposable
// instances with nothing to dispose; CA2000's leak warning does not apply.
#pragma warning disable CA2000

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
                Is.EqualTo(EventRecordDecoderRegistry.Default.StandardFields.Length));
            NodeId target = GetOfTypeNodeId(fake.LastFilter);
            Assert.That(target, Is.EqualTo(ObjectTypeIds.AlarmConditionType));
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
                    .SubscribeAlarmsAsync(s_notifier, registry: null, options: null, cts.Token)
                    .ConfigureAwait(false))
                {
                }
            }, Throws.InstanceOf<OperationCanceledException>());
        }

        private static Variant[] MakeConditionFields()
        {
            // Build a field array sized to the registry''s composed
            // StandardFields layout (server returns fields in that
            // order). Populate just the cells the test asserts on.
            var builder = new RegistryFieldBuilder();
            builder.Set(BrowseNames.EventId, Variant.From(new ByteString(new byte[] { 1 })));
            builder.Set(BrowseNames.EventType, Variant.From((NodeId)ObjectTypeIds.ConditionType));
            builder.Set(BrowseNames.SourceNode, Variant.From((NodeId)new NodeId(1u, 0)));
            builder.Set(BrowseNames.SourceName, Variant.From("Src"));
            builder.Set(BrowseNames.Time, Variant.From(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            builder.Set(BrowseNames.ReceiveTime, Variant.From(new DateTime(2024, 1, 1, 0, 0, 1, DateTimeKind.Utc)));
            builder.Set(BrowseNames.Message, Variant.From(new LocalizedText("en", "msg")));
            builder.Set(BrowseNames.Severity, Variant.From((ushort)100));
            builder.Set(BrowseNames.ConditionName, Variant.From("MyCondition"));
            builder.Set(BrowseNames.Retain, Variant.From(false));
            builder.SetNested(BrowseNames.EnabledState, BrowseNames.Id, Variant.From(true));
            return builder.Build();
        }

        private static Variant[] MakeDialogFields()
        {
            // Dialog records require a populated DialogState.Id.
            var builder = new RegistryFieldBuilder();
            builder.Set(BrowseNames.EventId, Variant.From(new ByteString(new byte[] { 1 })));
            builder.Set(BrowseNames.EventType, Variant.From((NodeId)ObjectTypeIds.DialogConditionType));
            builder.Set(BrowseNames.SourceNode, Variant.From((NodeId)new NodeId(2u, 0)));
            builder.Set(BrowseNames.SourceName, Variant.From("Src"));
            builder.Set(BrowseNames.Time, Variant.From(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            builder.Set(BrowseNames.ReceiveTime, Variant.From(new DateTime(2024, 1, 1, 0, 0, 1, DateTimeKind.Utc)));
            builder.Set(BrowseNames.Message, Variant.From(new LocalizedText("en", "msg")));
            builder.Set(BrowseNames.Severity, Variant.From((ushort)100));
            builder.Set(BrowseNames.ConditionName, Variant.From("MyDialog"));
            builder.Set(BrowseNames.Retain, Variant.From(true));
            builder.SetNested(BrowseNames.EnabledState, BrowseNames.Id, Variant.From(true));
            builder.SetNested(BrowseNames.DialogState, BrowseNames.Id, Variant.From(true));
            return builder.Build();
        }

        /// <summary>
        /// Builds a positional Variant[] matching the registry''s
        /// composed StandardFields layout, indexed by browse name.
        /// </summary>
        private sealed class RegistryFieldBuilder
        {
            private readonly Variant[] m_fields;

            public RegistryFieldBuilder()
            {
                m_fields = new Variant[EventRecordDecoderRegistry.Default.StandardFields.Length];
            }

            public void Set(string browseName, Variant value)
                => SetAt(FindIndex(new[] { QualifiedName.From(browseName) }), value);

            public void SetNested(string outer, string inner, Variant value)
                => SetAt(FindIndex(new[]
                {
                    QualifiedName.From(outer),
                    QualifiedName.From(inner)
                }), value);

            public Variant[] Build() => m_fields;

            private void SetAt(int index, Variant value)
            {
                if (index >= 0)
                {
                    m_fields[index] = value;
                }
            }

            private static int FindIndex(QualifiedName[] path)
            {
                QualifiedName[][] composed = EventRecordDecoderRegistry.Default.StandardFields;
                for (int i = 0; i < composed.Length; i++)
                {
                    if (composed[i].Length != path.Length)
                    {
                        continue;
                    }
                    bool match = true;
                    for (int j = 0; j < path.Length; j++)
                    {
                        if (!composed[i][j].Equals(path[j]))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        return i;
                    }
                }
                return -1;
            }
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
                MonitoringOptions? options = null,
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
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public async IAsyncEnumerable<DataValueChange> SubscribeDataChangesAsync(
                IReadOnlyList<NodeId> nodeIds,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
