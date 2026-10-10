/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotProjectedEventRuntimeTests
    {
        [Test]
        public async Task NamespacedEventIdRemainsBusinessData(
            [Values("integer", "bytes", "empty", "null")] string valueKind,
            [Values] bool nested)
        {
            string path = nested ? "Details/nsu=urn:vendor;EventId" : "nsu=urn:vendor;EventId";
            (WotProjectionBindingRuntimeTestHarness nodes,
                RecordingPublisher publisher,
                EventChannel upstream,
                IAsyncDisposable runtime) = await CreateOccurrenceRuntimeAsync(
                new WotResolvedEventSelectClause("nsu=urn:source;s=VendorEvent", path)).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable runtimeOwner = runtime.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId, cancellation.Token);
            await using ConfiguredAsyncDisposable eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            try
            {
                await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Variant value = BusinessEventId(valueKind);
                upstream.Push(OccurrenceNotification(value, 1, nested));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                ByteString firstId = events.Current.EventId!.Value;
                ushort vendorNamespace = (ushort)nodes.Builder.Context.NamespaceUris.GetIndex("urn:vendor");
                Assert.That(vendorNamespace, Is.GreaterThan((ushort)0));
                ArrayOf<QualifiedName> selectedPath = nested
                    ? [QualifiedName.From("Details"), new QualifiedName("EventId", vendorNamespace)]
                    : [new QualifiedName("EventId", vendorNamespace)];
                Variant businessValue = events.Current.GetAttributeValue(
                    Mock.Of<IFilterContext>(), events.Current.TypeDefinitionId,
                    selectedPath, Attributes.Value, default);
                Assert.That(businessValue, Is.EqualTo(value));
                Assert.That(firstId.IsEmpty, Is.False);

                pending = events.MoveNextAsync().AsTask();
                upstream.Push(OccurrenceNotification(value, 2, nested));
                upstream.Push(OccurrenceNotification(new Variant(new ByteString("\t"u8.ToArray())), 3, nested));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(Read(events.Current, "Severity").TryGetValue(out ushort sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(2));
                Assert.That(events.Current.EventId!.Value.IsEmpty, Is.False);
                Assert.That(events.Current.EventId.Value, Is.Not.EqualTo(firstId));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task UnselectedPayloadEventIdCannotControlOccurrences(
            [Values("integer", "bytes", "empty", "null")] string valueKind)
        {
            (WotProjectionBindingRuntimeTestHarness nodes,
                RecordingPublisher publisher,
                EventChannel upstream,
                IAsyncDisposable runtime) = await CreateOccurrenceRuntimeAsync(null).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable runtimeOwner = runtime.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId, cancellation.Token);
            await using ConfiguredAsyncDisposable eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            try
            {
                await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Variant value = BusinessEventId(valueKind);
                upstream.Push(OccurrenceNotification(value, 1));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                ByteString firstId = events.Current.EventId!.Value;
                pending = events.MoveNextAsync().AsTask();
                upstream.Push(OccurrenceNotification(value, 2));
                upstream.Push(OccurrenceNotification(new Variant(new ByteString("\t"u8.ToArray())), 3));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(Read(events.Current, "Severity").TryGetValue(out ushort sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(2));
                Assert.That(events.Current.EventId!.Value.IsEmpty, Is.False);
                Assert.That(events.Current.EventId.Value, Is.Not.EqualTo(firstId));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NamespaceZeroEventIdControlsOccurrenceDeduplication(
            [Values("EventId", "nsu=http://opcfoundation.org/UA/;EventId")] string path,
            [Values] bool derivedQueryType)
        {
            string anchor = derivedQueryType ? "nsu=urn:source;s=VendorEvent" : "i=2041";
            (WotProjectionBindingRuntimeTestHarness nodes,
                RecordingPublisher publisher,
                EventChannel upstream,
                IAsyncDisposable runtime) = await CreateOccurrenceRuntimeAsync(
                new WotResolvedEventSelectClause(anchor, path)).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable runtimeOwner = runtime.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId, cancellation.Token);
            await using ConfiguredAsyncDisposable eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            try
            {
                await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                ByteString sourceId = new(new byte[] { 1, 2, 3 });
                upstream.Push(OccurrenceNotification(new Variant(sourceId), 1));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                ByteString firstId = events.Current.EventId!.Value;
                Assert.That(firstId.IsEmpty, Is.False);
                Assert.That(firstId, Is.Not.EqualTo(sourceId));

                pending = events.MoveNextAsync().AsTask();
                upstream.Push(OccurrenceNotification(new Variant(sourceId), 2));
                upstream.Push(OccurrenceNotification(new Variant(new ByteString(new byte[] { 4, 5, 6 })), 3));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(Read(events.Current, "Severity").TryGetValue(out ushort sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(3));
                Assert.That(events.Current.EventId!.Value, Is.Not.EqualTo(firstId));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SelectedEventIdUsesItsMaterializedMemberPath()
        {
            (WotProjectionBindingRuntimeTestHarness nodes,
                RecordingPublisher publisher,
                EventChannel upstream,
                IAsyncDisposable runtime) = await CreateOccurrenceRuntimeAsync(
                new WotResolvedEventSelectClause("i=2041", "EventId"), includeEventIdChild: true).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable runtimeOwner = runtime.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId, cancellation.Token);
            await using ConfiguredAsyncDisposable eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            try
            {
                await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                upstream.Push(NotificationWithChild(1, 1));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                ByteString firstId = events.Current.EventId!.Value;
                pending = events.MoveNextAsync().AsTask();
                upstream.Push(NotificationWithChild(1, 2));
                upstream.Push(NotificationWithChild(2, 3));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(Read(events.Current, "Severity").TryGetValue(out ushort sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(3));
                Assert.That(events.Current.EventId!.Value, Is.Not.EqualTo(firstId));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }

            static WotNotification NotificationWithChild(byte eventId, ushort sequence)
            {
                return new WotNotification(new DataValue(Variant.Null), null,
                    new WotEventData(new Dictionary<string, WotEventData>(StringComparer.Ordinal)
                    {
                        ["EventId"] = new WotEventData(new Dictionary<string, WotEventData>(StringComparer.Ordinal)
                        {
                            ["Name"] = Field(new Variant(new ByteString(new[] { eventId }))),
                            ["Code"] = Field(new Variant(7))
                        }),
                        ["Severity"] = Field(new Variant(sequence))
                    }));
            }
        }

        [Test]
        public async Task NamespaceZeroEventIdRejectsInvalidOccurrenceValues(
            [Values("integer", "empty", "null")] string valueKind)
        {
            (WotProjectionBindingRuntimeTestHarness nodes,
                RecordingPublisher publisher,
                EventChannel upstream,
                IAsyncDisposable runtime) = await CreateOccurrenceRuntimeAsync(
                new WotResolvedEventSelectClause("i=2041", "EventId")).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable runtimeOwner = runtime.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId, cancellation.Token);
            await using ConfiguredAsyncDisposable eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            try
            {
                await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                upstream.Push(OccurrenceNotification(BusinessEventId(valueKind), 1));
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await pending.WaitAsync(s_timeout).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                Assert.That(upstream.Stopped, Is.EqualTo(1));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
        }

        private static async ValueTask<(
            WotProjectionBindingRuntimeTestHarness Nodes,
            RecordingPublisher Publisher,
            EventChannel Upstream,
            IAsyncDisposable Runtime)> CreateOccurrenceRuntimeAsync(
                WotResolvedEventSelectClause? occurrence, bool includeEventIdChild = false)
        {
            var nodes = new WotProjectionBindingRuntimeTestHarness();
            nodes.Builder.Context.NamespaceUris.Append("urn:unrelated");
            nodes.Builder.Context.NamespaceUris.Append("urn:vendor");
            BaseObjectTypeState eventType = nodes.AddEventType("Telemetry", Ua.ObjectTypeIds.BaseEventType);
            var sequence = new WotResolvedEventSelectClause("i=2041", "Severity");
            ArrayOf<WotResolvedEventSelectClause> clauses = occurrence is null ? [sequence] : [occurrence, sequence];
            if (includeEventIdChild)
            {
                clauses = clauses.AddItem(new WotResolvedEventSelectClause(
                    "nsu=urn:source;s=VendorEvent", "EventId/nsu=urn:vendor;Code"));
            }
            var selection = new WotEventSelection(clauses, WotEventSelectionOrigin.Standard);
            var form = new WotCompiledForm(
                new WotBindingIdentity("test", "1", "urn:test"), WotAffordanceKind.Event, "telemetry",
                "/events/telemetry/forms/0", WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                new WotEndpointDescriptor("test", null, -1, "test://occurrence"),
                new WotAddressingDescriptor("i=2253", ImmutableDictionary<string, string>.Empty),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/json", "json"), [], true, null, selection, null);
            var upstream = new EventChannel(form);
            nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            var publisher = new RecordingPublisher();
            var factory = new WotProjectionBindingRuntimeFactory(
                nodes.ChannelFactory, null, publisher, Mock.Of<IWotProjectionConditionFactory>(MockBehavior.Strict),
                new WotProjectionBindingRuntimeOptions());
            WotBindingPlan plan = WotProjectionBindingRuntimeTestHarness.Plan([form])
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        WotAffordanceKind.Event, "telemetry", "/events/telemetry",
                        eventType.NodeId.ToString(), nodes.Root.NodeId.ToString())
                ]);
            IAsyncDisposable runtime = await factory.CreateAsync(nodes.Builder, [plan]).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The event runtime must be present.");
            return (nodes, publisher, upstream, runtime);
        }

        private static WotNotification OccurrenceNotification(Variant eventId, ushort sequence, bool nested = false)
        {
            var members = new Dictionary<string, WotEventData>(StringComparer.Ordinal)
            {
                ["Severity"] = Field(new Variant(sequence))
            };
            if (nested)
            {
                members["Details"] = new WotEventData(new Dictionary<string, WotEventData>(StringComparer.Ordinal)
                {
                    ["EventId"] = Field(eventId)
                });
            }
            else
            {
                members["EventId"] = Field(eventId);
            }
            return new WotNotification(new DataValue(Variant.Null), null, new WotEventData(members));
        }

        private static Variant BusinessEventId(string kind)
        {
            return kind switch
            {
                "integer" => new Variant(7),
                "bytes" => new Variant(new ByteString(new byte[] { 1, 2, 3 })),
                "empty" => new Variant(ByteString.Empty),
                "null" => Variant.Null,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
    }
}
