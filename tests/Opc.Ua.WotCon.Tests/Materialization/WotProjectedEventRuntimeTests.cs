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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
#if NET8_0_OR_GREATER
using Opc.Ua.WotCon.Bindings.Http;
#endif
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Server.Materialization;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed partial class WotProjectedEventRuntimeTests
    {
        [Test]
        public async Task SharedSourceReportsDistinctConditionsAndRoutesAcknowledgeAndConfirm()
        {
            var h = new EventHarness();
            var (alarmA, acknowledgeA, _) = h.AddAlarm("a");
            var (_, acknowledgeB, _) = h.AddAlarm("b");
            var upstream = new EventChannel(alarmA);
            h.Nodes.ChannelFactory.SetChannel(alarmA, upstream.Channel);
            ByteString occurrence = new(new byte[] { 1, 2, 3 });
            ByteString acknowledgedOccurrence = new(new byte[] { 4, 5, 6 });
            h.Invokes["aAcknowledge"].OnInvoke = (inputs, _) =>
            {
                Assert.That(inputs, Has.Count.EqualTo(2));
                Assert.That(inputs[0].TryGetValue(out ByteString id) && id == occurrence, Is.True);
                Assert.That(inputs[1].TryGetValue(out LocalizedText comment) && comment.IsNull, Is.True);
                upstream.Push(Notification("a", acknowledgedOccurrence, acked: true));
                return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            };
            h.Invokes["aConfirm"].OnInvoke = (inputs, _) =>
            {
                Assert.That(inputs[0].TryGetValue(out ByteString id) && id == acknowledgedOccurrence, Is.True);
                Assert.That(inputs[1].TryGetValue(out LocalizedText comment), Is.True);
                Assert.That(comment.Text, Is.EqualTo("Confirmed remotely"));
                Assert.That(comment.Locale, Is.EqualTo("de-DE"));
                upstream.Push(Notification("a", new ByteString(new byte[] { 7, 8 }), acked: true, confirmed: true));
                return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            };

            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            Assert.That(h.Nodes.ChannelFactory.OpenCount, Is.Zero);
            Assert.That(h.Publisher.Roots, Is.EqualTo(new[] { h.Nodes.Root.NodeId }));
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            upstream.Push(Notification("a", occurrence));
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            BaseEventState first = events.Current;
            Assert.That(first.EventType!.Value, Is.EqualTo(h.Types["a"]));
            Assert.That(first.SourceNode!.Value, Is.EqualTo(h.Nodes.Root.NodeId));
            Assert.That(first.NodeId, Is.EqualTo(h.Conditions.Nodes["a"].NodeId));
            Assert.That(first.NodeId, Is.Not.EqualTo(h.Types["a"]));
            ByteString localId = first.EventId!.Value;
            Assert.That(localId, Is.Not.EqualTo(occurrence));
            Assert.That(Read(first, "ActiveState", "Id").TryGetValue(out bool active) && active, Is.True);

            ServiceResult wrongSource = await CallAsync(h, acknowledgeB, [new Variant(localId)]).ConfigureAwait(false);
            Assert.That(wrongSource.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(h.Invokes["bAcknowledge"].InvokeCount, Is.Zero);

            ServiceResult acknowledged = await CallAsync(h, acknowledgeA, [new Variant(localId)])
                .ConfigureAwait(false);
            Assert.That(acknowledged.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            BaseEventState afterAcknowledge = events.Current;
            Assert.That(Read(afterAcknowledge, "AckedState", "Id").TryGetValue(out bool acked) && acked, Is.True);
            Assert.That(Read(afterAcknowledge, "ConfirmedState", "Id")
                .TryGetValue(out bool confirmed) && !confirmed, Is.True);

            ConditionState condition = h.Conditions.Nodes["a"];
            MethodState? confirm = condition.FindMethod(
                h.Nodes.Builder.Context, Ua.MethodIds.AcknowledgeableConditionType_Confirm);
            Assert.That(confirm, Is.Not.Null);
            ServiceResult confirmation = await confirm!.CallAsync(
                h.Nodes.Builder.Context, condition.NodeId,
                [
                    new Variant(afterAcknowledge.EventId!.Value),
                    new Variant(new LocalizedText("de-DE", "Confirmed remotely"))
                ], [], [])
                .ConfigureAwait(false);
            Assert.That(confirmation.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            Assert.That(Read(events.Current, "ConfirmedState", "Id").TryGetValue(out confirmed) && confirmed, Is.True);
            Assert.That(upstream.Channel.SubscribeEventCount, Is.EqualTo(1));
            Assert.That(h.Invokes["aAcknowledge"].InvokeCount, Is.EqualTo(1));
            Assert.That(h.Invokes["aConfirm"].InvokeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DuplicateOccurrencesAreNotDeliveredAndRejectedOccurrencesCannotBeAcknowledged()
        {
            var h = new EventHarness(maxRoutes: 1);
            var (form, acknowledge, _) = h.AddAlarm("a");
            var upstream = new EventChannel(form);
            h.Nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            h.Invokes["aAcknowledge"].OnInvoke = (_, _) =>
                new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            WotNotification first = Notification("a", new ByteString(new byte[] { 1 }));
            upstream.Push(first);
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString acceptedId = events.Current.EventId!.Value;

            upstream.Push(first);
            upstream.Push(Notification("a", new ByteString(new byte[] { 2 }), acked: true));
            ServiceResultException? capacity = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(capacity!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(Read(h.Conditions.Nodes["a"], "AckedState", "Id").TryGetValue(out bool acked), Is.True);
            Assert.That(acked, Is.False);
            ServiceResult accepted = await CallAsync(h, acknowledge, [new Variant(acceptedId)]).ConfigureAwait(false);
            Assert.That(accepted.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult result = await CallAsync(
                h, acknowledge, [new Variant(new ByteString(new byte[] { 2 }))]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(h.Invokes["aAcknowledge"].InvokeCount, Is.EqualTo(1));
            Assert.That(upstream.Stopped, Is.EqualTo(1));
            IAsyncEnumerator<BaseEventState> retry = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var retryOwner = retry.ConfigureAwait(false);
            ServiceResultException? unavailable = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await retry.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(unavailable!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(upstream.Channel.SubscribeEventCount, Is.EqualTo(1));
        }

        [Test]
        public async Task LastConsumerStopsSubscriptionButRetainsRoutesAndChannelUntilGenerationDisposal()
        {
            var h = new EventHarness();
            var (form, acknowledge, _) = h.AddAlarm("a");
            var upstream = new EventChannel(form);
            h.Nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            h.Invokes["aAcknowledge"].OnInvoke = (_, _) =>
                new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.BadUserAccessDenied));
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            ByteString localId;
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using (events.ConfigureAwait(false))
            {
                Task<bool> pending = events.MoveNextAsync().AsTask();
                await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                upstream.Push(Notification("a", new ByteString(new byte[] { 1 })));
                Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                localId = events.Current.EventId!.Value;
            }
            Assert.That(upstream.Stopped, Is.EqualTo(1));
            Assert.That(upstream.Channel.DisposeCount, Is.Zero);
            ServiceResult denied = await CallAsync(h, acknowledge, [new Variant(localId)]).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(h.Invokes["aAcknowledge"].InvokeCount, Is.EqualTo(1));

            await runtime.DisposeAsync().ConfigureAwait(false);
            Assert.That(upstream.Channel.DisposeCount, Is.EqualTo(1));
            Assert.That(h.Invokes["aAcknowledge"].DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SourcesWithDifferentResolvedPayloadTypesNeverShareASubscription()
        {
            var h = new EventHarness();
            _ = h.AddAlarm("typed-a");
            _ = h.AddAlarm("typed-b");
            using WotDocument firstDocument = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """{"type":"object","properties":{"Reading":{"type":"integer","uav:dataTypeId":"i=5"}}}"""));
            using WotDocument secondDocument = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """{"type":"object","properties":{"Reading":{"type":"string","uav:dataTypeId":"i=12"}}}"""));
            WotPayloadSchema firstSchema = WotNodeSetConverter.CapturePayloadSchema(
                firstDocument, Wot.WotAffordanceKind.Property, firstDocument.RootElement);
            WotPayloadSchema secondSchema = WotNodeSetConverter.CapturePayloadSchema(
                secondDocument, Wot.WotAffordanceKind.Property, secondDocument.RootElement);
            var firstSelection = new WotEventSelection(
                s_selection.Clauses.AddItem(
                    new WotResolvedEventSelectClause("i=2915", "Reading").WithPayloadSchema(firstSchema)),
                WotEventSelectionOrigin.Standard);
            var secondSelection = new WotEventSelection(
                s_selection.Clauses.AddItem(
                    new WotResolvedEventSelectClause("i=2915", "Reading").WithPayloadSchema(secondSchema)),
                WotEventSelectionOrigin.Standard);
            var first = new EventChannel(h.SetSelection("typed-a", firstSelection));
            var second = new EventChannel(h.SetSelection("typed-b", secondSelection));
            h.Nodes.ChannelFactory.SetChannel(first.Channel.Form, first.Channel);
            h.Nodes.ChannelFactory.SetChannel(second.Channel.Form, second.Channel);
            await using IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId, cancellation.Token);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> next = events.MoveNextAsync().AsTask();
            try
            {
                await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(s_timeout).ConfigureAwait(false);
                first.Push(WithReading("typed-a", new ByteString(new byte[] { 1 }), new Variant((ushort)17)));
                Assert.That(await next.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(events.Current.EventType!.Value, Is.EqualTo(h.Types["typed-a"]));
                Assert.That(Read(events.Current, "Reading").TryGetValue(out ushort number), Is.True);
                Assert.That(number, Is.EqualTo((ushort)17));
                next = events.MoveNextAsync().AsTask();
                second.Push(WithReading("typed-b", new ByteString(new byte[] { 2 }), new Variant("separate schema")));
                Assert.That(await next.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                Assert.That(events.Current.EventType!.Value, Is.EqualTo(h.Types["typed-b"]));
                Assert.That(Read(events.Current, "Reading").TryGetValue(out string? text), Is.True);
                Assert.That(text, Is.EqualTo("separate schema"));
                Assert.That(first.Channel.SubscribeEventCount, Is.EqualTo(1));
                Assert.That(second.Channel.SubscribeEventCount, Is.EqualTo(1));
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                try
                {
                    await next.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Drain the pending read before the test disposes its enumerator.
                }
            }

            static WotNotification WithReading(string condition, ByteString eventId, Variant value)
            {
                WotNotification original = Notification(condition, eventId);
                var members = original.Data.Members.ToDictionary(pair => pair.Key, pair => pair.Value);
                members.Add("Reading", Field(value));
                return new WotNotification(original.Value, null, new WotEventData(members), original.NamespaceUris);
            }
        }

        [Test]
        public async Task IncompleteNotificationFaultsTheStreamAndDisposesItsSubscription()
        {
            var h = new EventHarness();
            var (form, _, _) = h.AddAlarm("a");
            var upstream = new EventChannel(form);
            h.Nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            upstream.Push(new WotNotification(new DataValue(Variant.Null)));
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await pending.WaitAsync(s_timeout).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(upstream.Stopped, Is.EqualTo(1));
        }

        [Test]
        public async Task SubscriptionFailurePropagatesAndItsChannelRemainsGenerationOwned()
        {
            var h = new EventHarness();
            var (form, _, _) = h.AddAlarm("a");
            var channel = new FakeWotBindingChannel(form)
            {
                OnSubscribeEvent = (_, _) => new ValueTask<IWotSubscription>(
                    Task.FromException<IWotSubscription>(new ServiceResultException(StatusCodes.BadNoCommunication)))
            };
            h.Nodes.ChannelFactory.SetChannel(form, channel);
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            ServiceResultException? error = null;

            try
            {
                await events.MoveNextAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                error = exception;
            }

            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
            Assert.That(channel.SubscribeEventCount, Is.EqualTo(1));
            Assert.That(channel.DisposeCount, Is.Zero);
            await runtime.DisposeAsync().ConfigureAwait(false);
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        [TestCase("source-b", 4840)]
        [TestCase("source-a", 4841)]
        public async Task ConditionActionWithEqualEmptyBaseUriCannotSelectAnotherEndpoint(string host, int port)
        {
            var h = new EventHarness();
            h.AddAlarm("a", string.Empty);
            var source = new WotEndpointDescriptor("test", "source-a", 4840, string.Empty);
            h.SetEndpoint("a", source);
            h.SetEndpoint("aConfirm", source);
            h.SetEndpoint("aAcknowledge", new WotEndpointDescriptor("test", host, port, string.Empty));
            ServiceResultException? error = null;
            IAsyncDisposable? unexpectedRuntime = null;

            try
            {
                unexpectedRuntime = await h.WireAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                error = exception;
            }
            finally
            {
                if (unexpectedRuntime is not null)
                {
                    await unexpectedRuntime.DisposeAsync().ConfigureAwait(false);
                }
            }

            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(error.Message, Does.Contain("/actions/aAcknowledge"));
            Assert.That(h.Publisher.Roots, Is.Empty);
            Assert.That(h.Nodes.ChannelFactory.OpenCount, Is.Zero);
        }

        [Test]
        public async Task IdenticalUpstreamEventIdsFromDifferentSourcesCannotCrossRoute()
        {
            var h = new EventHarness();
            var (formA, _, _) = h.AddAlarm("a", "test://one");
            var (formB, acknowledgeB, _) = h.AddAlarm("b", "test://two");
            var sourceA = new EventChannel(formA);
            var sourceB = new EventChannel(formB);
            h.Nodes.ChannelFactory.SetChannel(formA, sourceA.Channel);
            h.Nodes.ChannelFactory.SetChannel(formB, sourceB.Channel);
            ByteString original = new(new byte[] { 9 });
            h.Invokes["bAcknowledge"].OnInvoke = (inputs, _) =>
            {
                Assert.That(inputs[0].TryGetValue(out ByteString id) && id == original, Is.True);
                return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            };
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await Task.WhenAll(sourceA.Started.Task, sourceB.Started.Task).WaitAsync(s_timeout).ConfigureAwait(false);
            sourceA.Push(Notification("a", original));
            sourceB.Push(Notification("b", original));
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString idA = events.Current.EventId!.Value;
            Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString idB = events.Current.EventId!.Value;
            Assert.That(idA, Is.Not.EqualTo(idB));

            ServiceResult wrong = await CallAsync(h, acknowledgeB, [new Variant(idA)]).ConfigureAwait(false);
            Assert.That(wrong.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            ServiceResult right = await CallAsync(h, acknowledgeB, [new Variant(idB)]).ConfigureAwait(false);
            Assert.That(right.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(h.Invokes["aAcknowledge"].InvokeCount, Is.Zero);
            Assert.That(h.Invokes["bAcknowledge"].InvokeCount, Is.EqualTo(1));
            Assert.That(sourceA.Channel.SubscribeEventCount, Is.EqualTo(1));
            Assert.That(sourceB.Channel.SubscribeEventCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RetiringGenerationRoutesRemainAvailableUntilThatGenerationDisposes()
        {
            var old = new EventHarness();
            var (oldForm, _, _) = old.AddAlarm("a");
            var upstream = new EventChannel(oldForm);
            old.Nodes.ChannelFactory.SetChannel(oldForm, upstream.Channel);
            var factory = new WotProjectionBindingRuntimeFactory(
                old.Nodes.ChannelFactory, null, old.Publisher, old.Conditions,
                new WotProjectionBindingRuntimeOptions());
            IAsyncDisposable oldRuntime = await old.WireAsync(factory).ConfigureAwait(false);
            await using var oldOwner = oldRuntime.ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = old.Publisher.Open(old.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            ByteString original = new(new byte[] { 21 });
            upstream.Push(Notification("a", original));
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString local = events.Current.EventId!.Value;

            var current = new EventHarness();
            var (_, acknowledge, _) = current.AddAlarm("a");
            foreach (FakeWotBindingChannel channel in current.Invokes.Values)
            {
                old.Nodes.ChannelFactory.SetChannel(channel.Form, channel);
            }
            current.Invokes["aAcknowledge"].OnInvoke = (inputs, _) =>
            {
                Assert.That(inputs[0].TryGetValue(out ByteString id) && id == original, Is.True);
                return new ValueTask<WotInvokeResult>(new WotInvokeResult(StatusCodes.Good));
            };
            IAsyncDisposable currentRuntime = await current.WireAsync(factory).ConfigureAwait(false);
            await using var currentOwner = currentRuntime.ConfigureAwait(false);

            ServiceResult whileRetiring = await CallAsync(current, acknowledge, [new Variant(local)])
                .ConfigureAwait(false);
            Assert.That(whileRetiring.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(upstream.Channel.DisposeCount, Is.Zero);
            await oldRuntime.DisposeAsync().ConfigureAwait(false);
            ServiceResult afterRetirement = await CallAsync(current, acknowledge, [new Variant(local)])
                .ConfigureAwait(false);
            Assert.That(afterRetirement.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(upstream.Stopped, Is.EqualTo(1));
            Assert.That(upstream.Channel.DisposeCount, Is.EqualTo(1));
            Assert.That(current.Invokes["aAcknowledge"].InvokeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DeclaredAncestorNotifierReceivesOneEventAndGatesTheLeafSource()
        {
            var h = new EventHarness();
            BaseObjectState leaf = h.Nodes.AddDetachedObject("leaf");
            leaf.AddReference(Ua.ReferenceTypeIds.HasNotifier, true, h.Nodes.Root.NodeId);
            var (form, _, _) = h.AddAlarm("a", notifier: leaf);
            var upstream = new EventChannel(form);
            h.Nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            Assert.That(h.Publisher.Roots, Is.EqualTo(new[] { h.Nodes.Root.NodeId }));
            Assert.That(leaf.AreEventsMonitored, Is.False);
            h.Nodes.Root.SetAreEventsMonitored(h.Nodes.Builder.Context, true, true);
            Assert.That(leaf.AreEventsMonitored, Is.True);
            int delivered = 0;
            h.Nodes.Root.OnReportEvent = (_, _, _) => delivered++;
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(leaf.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            upstream.Push(Notification("a", new ByteString(new byte[] { 1 })));
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            leaf.ReportEvent(h.Nodes.Builder.Context, events.Current);
            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(upstream.Channel.SubscribeEventCount, Is.EqualTo(1));
        }

#if NET8_0_OR_GREATER
        [TestCase(false)]
        [TestCase(true)]
        public async Task HttpEventSelectedFieldsReachProjectedConsumer(bool customSelection)
        {
            string sourceEventType = customSelection ? "i=2915" : "i=2041";
            string extraData = customSelection
                ? """, "ActiveState": { "Id": true }"""
                : string.Empty;
            string payload = $$"""
                {
                  "EventId": "AQID",
                  "EventType": "{{sourceEventType}}",
                  "SourceNode": "i=2253",
                  "SourceName": "boiler-7",
                  "Time": "2026-08-01T12:00:00Z",
                  "ReceiveTime": "2026-08-01T12:00:01Z",
                  "Message": "Over limit",
                  "Severity": 700{{extraData}}
                }
                """;
            int sendCount = 0;
            HttpMethod? receivedMethod = null;
            Uri? receivedUri = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
                {
                    Interlocked.Increment(ref sendCount);
                    receivedMethod = request.Method;
                    receivedUri = request.RequestUri;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                });
            using var client = new HttpClient(handler.Object);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    ClientFactory = () => client,
                    CallerClientHandlesRedirectSafety = true,
                    ObserveInterval = TimeSpan.FromHours(1)
                })]);
            string extraSchema = customSelection
                ? """
                  ,
                  "ActiveState": {
                    "type": "object",
                    "properties": { "Id": { "type": "boolean" } }
                  }
                  """
                : string.Empty;
            string selectionReference = customSelection ? "\"tm:ref\": \"urn:http-tests:AlarmType\"," : string.Empty;
            string td = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Projected HTTP events",
                  "events": {
                    "alarm": {
                      {{selectionReference}}
                      "data": {
                        "type": "object",
                        "properties": {
                          "EventId": { "type": "string", "contentEncoding": "base64" },
                          "EventType": { "type": "string" },
                          "SourceNode": { "type": "string" },
                          "SourceName": { "type": "string" },
                          "Time": { "type": "string", "format": "date-time" },
                          "ReceiveTime": { "type": "string", "format": "date-time" },
                          "Message": { "type": "string" },
                          "Severity": { "type": "integer", "minimum": 0, "maximum": 65535 }
                          {{extraSchema}}
                        }
                      },
                      "forms": [{
                        "href": "https://http-payloads.example/events",
                        "op": "subscribeevent",
                        "contentType": "application/json"
                      }]
                    }
                  }
                }
                """;
            WotEventSelectionCatalog selections = customSelection
                ? WotEventSelectionCatalog.Create(
                    new Dictionary<string, ArrayOf<WotResolvedEventSelectClause>>(StringComparer.Ordinal)
                    {
                        ["alarm"] =
                        [
                            .. WotEventSelection.Default.Clauses,
                            new WotResolvedEventSelectClause("i=2915", "ActiveState/Id")
                        ]
                    })
                : WotEventSelectionCatalog.Empty;
            var nodes = new WotProjectionBindingRuntimeTestHarness();
            BaseObjectTypeState type = nodes.AddEventType("HttpEventType", Ua.ObjectTypeIds.BaseEventType);
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "http-events", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td), selections))
                .WithProjectedAffordances(
                [
                    new WotProjectedAffordance(
                        WotAffordanceKind.Event, "alarm", "/events/alarm",
                        type.NodeId.ToString(), nodes.Root.NodeId.ToString())
                ]);
            WotCompiledForm form = plan.CompiledForms.Single(
                compiled => compiled.Operation == WoTBindingCapabilityEnum.SubscribeEvent);
            var publisher = new RecordingPublisher();
            var runtimeFactory = new WotProjectionBindingRuntimeFactory(
                registry, null, publisher, Mock.Of<IWotProjectionConditionFactory>(MockBehavior.Strict),
                new WotProjectionBindingRuntimeOptions());

            IAsyncDisposable runtime = await runtimeFactory.CreateAsync(nodes.Builder, [plan]).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The HTTP event runtime must be present.");
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            Assert.That(publisher.Roots, Is.EqualTo(new[] { nodes.Root.NodeId }));
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId);
            await using (events.ConfigureAwait(false))
            {
                Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                BaseEventState projected = events.Current;
                Assert.That(projected.EventId!.Value.IsEmpty, Is.False);
                Assert.That(projected.EventId.Value, Is.Not.EqualTo(new ByteString(new byte[] { 1, 2, 3 })));
                Assert.That(projected.EventType!.Value, Is.EqualTo(type.NodeId));
                Assert.That(projected.SourceNode!.Value, Is.EqualTo(nodes.Root.NodeId));
                Assert.That(projected.SourceName!.Value, Is.EqualTo("boiler-7"));
                Assert.That(projected.Time!.Value, Is.EqualTo(new DateTimeUtc(2026, 8, 1, 12, 0, 0)));
                Assert.That(projected.ReceiveTime!.Value, Is.EqualTo(new DateTimeUtc(2026, 8, 1, 12, 0, 1)));
                Assert.That(projected.Message!.Value.Text, Is.EqualTo("Over limit"));
                Assert.That(projected.Severity!.Value, Is.EqualTo((ushort)700));
                if (customSelection)
                {
                    Assert.That(Read(projected, "ActiveState", "Id").TryGetValue(out bool active), Is.True);
                    Assert.That(active, Is.True);
                }

                WotEventSelection selection = form.EventSelection ?? WotEventSelection.Default;
                Assert.That(
                    selection.Origin,
                    Is.EqualTo(customSelection ? WotEventSelectionOrigin.Standard : WotEventSelectionOrigin.Default));
                string[] expectedPaths = customSelection
                    ? [
                        "EventId", "EventType", "SourceNode", "SourceName",
                        "Time", "ReceiveTime", "Message", "Severity", "ActiveState/Id"
                    ]
                    : [
                        "EventId", "EventType", "SourceNode", "SourceName", "Time", "ReceiveTime", "Message", "Severity"
                    ];
                Assert.That(selection.Clauses.ConvertAll(clause => clause.BrowsePath), Is.EqualTo(expectedPaths));
            }
            Assert.That(sendCount, Is.EqualTo(1));
            Assert.That(receivedMethod, Is.EqualTo(HttpMethod.Get));
            Assert.That(receivedUri, Is.EqualTo(new Uri("https://http-payloads.example/events")));
        }

        [TestCase("valid")]
        [TestCase("malformed")]
        [TestCase("missing")]
        [TestCase("wrong-kind")]
        public async Task AuthoredHttpEventPayloadReachesRealConsumerOrFailsExplicitly(string scenario)
        {
            const string definition = """
                {
                  "@context":{"native":"http://opcfoundation.org/UA/","model":"urn:http-event-fields"},
                  "@type":["tm:ThingModel","uav:eventType"],
                  "uav:id":"nsu=urn:http-event-types;s=Telemetry","title":"Telemetry event",
                  "data":{
                    "type":"object",
                    "uav:fieldOrder":[
                      "EventId","EventType","SourceNode","SourceName",
                      "Time","ReceiveTime","Message","Severity","Details"
                    ],
                    "properties":{
                      "EventId":{"type":"string","contentEncoding":"base64"},
                      "EventType":{"type":"string","uav:dataTypeId":"i=17"},
                      "SourceNode":{"type":"string","uav:dataTypeId":"i=17"},
                      "SourceName":{"type":"string"},
                      "Time":{"type":"string","format":"date-time"},
                      "ReceiveTime":{"type":"string","format":"date-time"},
                      "Message":{"type":"string","uav:dataTypeId":"i=21"},
                      "Severity":{"type":"integer","uav:dataTypeId":"i=5"},
                      "Details":{
                        "type":"object","uav:browseName":"model:Details","uav:fieldOrder":["Target","Pressure"],
                        "properties":{
                          "Target":{"type":"string","uav:dataTypeName":"native:NodeId","uav:browseName":"model:Target"},
                          "Pressure":{
                            "type":"integer","uav:dataTypeName":"native:UInt16","uav:browseName":"model:Pressure"
                          }
                        }
                      }
                    }
                  }
                }
                """;
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.Setup(value => value.ResolveThingAsync(
                    "event-type.tm.json", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolverResult>(
                    WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(definition))));
            const string td = """
                {
                  "@context":{"fields":"urn:http-event-fields","native":"urn:wrong-referrer"},
                  "title":"Authored HTTP event",
                  "events":{
                    "telemetry":{
                      "uav:eventSelectClauses":[
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"EventId"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"EventType"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"SourceNode"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"SourceName"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"Time"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"ReceiveTime"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"Message"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"Severity"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"fields:Details/fields:Target"},
                        {"tm:ref":"event-type.tm.json","uav:browsePath":"fields:Details/fields:Pressure"}
                      ],
                      "forms":[{"href":"https://http-payloads.example/authored","op":"subscribeevent"}]
                    }
                  }
                }
                """;
            string details = scenario switch
            {
                "missing" => """{"Pressure":65535}""",
                "wrong-kind" => """{"Target":"nsu=urn:remote-event;s=Target","Pressure":"65535"}""",
                _ => """{"Target":"nsu=urn:remote-event;s=Target","Pressure":65535}"""
            };
            string payload = scenario == "malformed" ? "{" : $$"""
                {
                  "EventId":"AQID","EventType":"nsu=urn:http-event-types;s=Telemetry",
                  "SourceNode":"nsu=urn:remote-event;s=Source","SourceName":"remote-device",
                  "Time":"2026-08-01T12:00:00Z","ReceiveTime":"2026-08-01T12:00:01Z",
                  "Message":"Scoped fields","Severity":700,"Details":{{details}}
                }
                """;
            int sends = 0;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected().Setup<Task<HttpResponseMessage>>(
                    "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
                {
                    Interlocked.Increment(ref sends);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                });
            using var client = new HttpClient(handler.Object);
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(new HttpWotBindingOptions
                {
                    ClientFactory = () => client,
                    CallerClientHandlesRedirectSafety = true,
                    ObserveInterval = TimeSpan.FromHours(1),
                    RetryPolicy = new ExponentialBackoffChannelReconnectPolicy { MaxAttempts = 1 }
                })]);
            var diagnostics = new List<WotDiagnostic>();
            WotBindingPlanRequest request = await WotBindingPlanRequest.FromDocumentAsync(
                "authored-http", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td),
                resolver.Object, diagnostics: diagnostics).ConfigureAwait(false);
            Assert.That(diagnostics.Where(value => value.Severity == WotDiagnosticSeverity.Error), Is.Empty);
            var nodes = new WotProjectionBindingRuntimeTestHarness();
            nodes.Builder.Context.NamespaceUris.Append("urn:unrelated-local");
            nodes.Builder.Context.NamespaceUris.Append("urn:http-event-fields");
            Assert.That(nodes.Builder.Context.NamespaceUris.GetIndex("urn:remote-event"), Is.EqualTo(-1));
            BaseObjectTypeState type = nodes.AddEventType("AuthoredHttpEvent", Ua.ObjectTypeIds.BaseEventType);
            WotBindingPlan plan = registry.Prepare(request).WithProjectedAffordances(
                [new WotProjectedAffordance(WotAffordanceKind.Event, "telemetry", "/events/telemetry",
                    type.NodeId.ToString(), nodes.Root.NodeId.ToString())]);
            WotCompiledForm form = plan.CompiledForms.Single();
            Assert.That(form.EventSelection!.Clauses, Has.Count.EqualTo(10));
            Assert.That(form.EventSelection.Clauses[8].Source, Is.EqualTo(WotEventSelectClauseSource.Explicit));
            Assert.That(form.EventSelection.Clauses[8].BrowsePath,
                Is.EqualTo("nsu=urn:http-event-fields;Details/nsu=urn:http-event-fields;Target"));
            var publisher = new RecordingPublisher();
            var factory = new WotProjectionBindingRuntimeFactory(
                registry, null, publisher, Mock.Of<IWotProjectionConditionFactory>(MockBehavior.Strict),
                new WotProjectionBindingRuntimeOptions());
            await using IAsyncDisposable runtime = await factory.CreateAsync(nodes.Builder, [plan])
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("The authored HTTP event runtime must be present.");
            IAsyncEnumerator<BaseEventState> events = publisher.Open(nodes.Root.NodeId);
            await using (events.ConfigureAwait(false))
            {
                if (scenario == "valid")
                {
                    Assert.That(
                        await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
                    BaseEventState projected = events.Current;
                    ushort ns = nodes.Builder.Context.NamespaceUris.GetIndexOrAppend("urn:http-event-fields");
                    Variant target = projected.GetAttributeValue(
                        Mock.Of<IFilterContext>(), Ua.ObjectTypeIds.BaseEventType,
                        [new QualifiedName("Details", ns), new QualifiedName("Target", ns)], Attributes.Value, default);
                    Variant pressure = projected.GetAttributeValue(
                        Mock.Of<IFilterContext>(), Ua.ObjectTypeIds.BaseEventType,
                        [new QualifiedName("Details", ns), new QualifiedName("Pressure", ns)],
                        Attributes.Value, default);
                    Assert.That(target.TryGetValue(out NodeId targetNode), Is.True);
                    Assert.That(NodeId.ToExpandedNodeId(targetNode, nodes.Builder.Context.NamespaceUris),
                        Is.EqualTo(new ExpandedNodeId("Target", "urn:remote-event")));
                    Assert.That(pressure.TryGetValue(out ushort value), Is.True);
                    Assert.That(value, Is.EqualTo(ushort.MaxValue));
                    Assert.That(projected.EventType!.Value, Is.EqualTo(type.NodeId));
                    Assert.That(projected.SourceNode!.Value, Is.EqualTo(nodes.Root.NodeId));
                    Assert.That(projected.SourceName!.Value, Is.EqualTo("remote-device"));
                    Assert.That(projected.Message!.Value.Text, Is.EqualTo("Scoped fields"));
                    Assert.That(projected.Time!.Value, Is.EqualTo(new DateTimeUtc(2026, 8, 1, 12, 0, 0)));
                }
                else
                {
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false))!;
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
                }
            }
            await runtime.DisposeAsync().ConfigureAwait(false);

            Assert.That(sends, Is.EqualTo(1));
            resolver.Verify(value => value.ResolveThingAsync(
                "event-type.tm.json", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            using HttpResponseMessage probe = await client.GetAsync(new Uri("https://http-payloads.example/probe"))
                .ConfigureAwait(false);
            Assert.That(probe.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sends, Is.EqualTo(2));
        }
#endif

        private static ValueTask<ServiceResult> CallAsync(EventHarness h, MethodState method, ArrayOf<Variant> inputs)
        {
            return method.CallAsync(h.Nodes.Builder.Context, h.Nodes.Root.NodeId, inputs, [], []);
        }

        private static Variant Read(BaseEventState state, params string[] names)
        {
            var path = new QualifiedName[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                path[i] = QualifiedName.From(names[i]);
            }
            return state.GetAttributeValue(
                Mock.Of<IFilterContext>(), Ua.ObjectTypeIds.BaseEventType, path, Attributes.Value, default);
        }

        private static WotNotification Notification(
            string condition, ByteString eventId, bool acked = false, bool confirmed = false)
        {
            var timestamp = new DateTimeUtc(2026, 8, 1, 0, 0, 0);
            var members = new Dictionary<string, WotEventData>(StringComparer.Ordinal)
            {
                ["EventId"] = Field(new Variant(eventId)),
                ["EventType"] = Field(new Variant(Ua.ObjectTypeIds.AlarmConditionType)),
                ["SourceNode"] = Field(new Variant(new NodeId("source", 1))),
                ["SourceName"] = Field(new Variant("source")),
                ["Time"] = Field(new Variant(timestamp)),
                ["ReceiveTime"] = Field(new Variant(timestamp)),
                ["Message"] = Field(new Variant(new LocalizedText("Alarm"))),
                ["Severity"] = Field(new Variant((ushort)500)),
                ["ConditionId"] = Field(new Variant(new NodeId(condition, 1))),
                ["BranchId"] = Field(new Variant(NodeId.Null)),
                ["AckedState"] = new WotEventData(new Dictionary<string, WotEventData>
                {
                    ["Id"] = Field(new Variant(acked))
                }),
                ["ConfirmedState"] = new WotEventData(new Dictionary<string, WotEventData>
                {
                    ["Id"] = Field(new Variant(confirmed))
                }),
                ["ActiveState"] = new WotEventData(new Dictionary<string, WotEventData>
                {
                    ["Id"] = Field(new Variant(true))
                })
            };
            return new WotNotification(
                new DataValue(Variant.Null, StatusCodes.Good, timestamp, timestamp),
                null, new WotEventData(members), [Ua.Namespaces.OpcUa, "urn:source"]);
        }

        private static WotEventData Field(Variant value)
        {
            return new WotEventData(new DataValue(value));
        }

        private sealed class EventHarness
        {
            public EventHarness(int maxRoutes = 4096)
            {
                Conditions = new RecordingConditionFactory(Nodes);
                m_maxRoutes = maxRoutes;
            }

            public WotProjectionBindingRuntimeTestHarness Nodes { get; } = new();

            public RecordingPublisher Publisher { get; } = new();

            public RecordingConditionFactory Conditions { get; }

            public Dictionary<string, NodeId> Types { get; } = new(StringComparer.Ordinal);

            public Dictionary<string, FakeWotBindingChannel> Invokes { get; } = new(StringComparer.Ordinal);

            public (WotCompiledForm Form, MethodState Acknowledge, MethodState Confirm) AddAlarm(
                string name, string endpoint = "test://source", BaseObjectState? notifier = null)
            {
                notifier ??= Nodes.Root;
                BaseObjectTypeState type = Nodes.AddEventType(name + "Type", Ua.ObjectTypeIds.AlarmConditionType);
                Types.Add(name, type.NodeId);
                WotCompiledForm form = Form(WotAffordanceKind.Event, name, "i=2253", null, endpoint);
                m_forms.Add(form);
                m_declarations.Add(new WotProjectedAffordance(
                    WotAffordanceKind.Event, name, "/events/" + name, type.NodeId.ToString(),
                    notifier.NodeId.ToString(), conditionTypeId: "i=2915"));
                MethodState acknowledge = AddAction(name, "Acknowledge", endpoint, notifier, includeComment: false);
                MethodState confirm = AddAction(name, "Confirm", endpoint, notifier, includeComment: true);
                return (form, acknowledge, confirm);
            }

            public async ValueTask<IAsyncDisposable> WireAsync(WotProjectionBindingRuntimeFactory? factory = null)
            {
                factory ??= new WotProjectionBindingRuntimeFactory(
                    Nodes.ChannelFactory, null, Publisher, Conditions,
                    new WotProjectionBindingRuntimeOptions { MaxEventRoutes = m_maxRoutes });
                WotBindingPlan plan = WotProjectionBindingRuntimeTestHarness.Plan([.. m_forms])
                    .WithProjectedAffordances(m_declarations.ToArrayOf());
                return await factory.CreateAsync(Nodes.Builder, [plan]).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The runtime must be present.");
            }

            public void SetEndpoint(string name, WotEndpointDescriptor endpoint)
            {
                int index = m_forms.FindIndex(form => form.AffordanceName == name);
                WotCompiledForm form = m_forms[index];
                m_forms[index] = new WotCompiledForm(
                    form.Binding, form.AffordanceKind, form.AffordanceName, form.JsonPointer,
                    form.Operation, form.OpToken, endpoint, form.Addressing, form.OperationInfo,
                    form.Payload, form.Security, form.IsExecutable, form.TargetMapping,
                    form.EventSelection, form.SecurityFloor)
                    .WithConditionInvocation(form.ConditionInvocation);
            }

            public WotCompiledForm SetSelection(string name, WotEventSelection selection)
            {
                int index = m_forms.FindIndex(form => form.AffordanceName == name);
                WotCompiledForm form = m_forms[index];
                var updated = new WotCompiledForm(
                    form.Binding, form.AffordanceKind, form.AffordanceName, form.JsonPointer,
                    form.Operation, form.OpToken, form.Endpoint, form.Addressing, form.OperationInfo,
                    form.Payload, form.Security, form.IsExecutable, form.TargetMapping, selection, form.SecurityFloor);
                m_forms[index] = updated;
                return updated;
            }

            private MethodState AddAction(
                string eventName, string action, string endpoint, BaseObjectState notifier, bool includeComment)
            {
                string name = eventName + action;
                ArrayOf<Argument> arguments =
                [
                    new Argument
                    {
                        Name = "EventId", DataType = Ua.DataTypeIds.ByteString, ValueRank = ValueRanks.Scalar
                    }
                ];
                if (includeComment)
                {
                    arguments = arguments.AddItem(new Argument
                    {
                        Name = "Comment",
                        DataType = Ua.DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.Scalar
                    });
                }
                MethodState method = Nodes.AddMethod(name, arguments, [], notifier);
                WotCompiledForm form = Form(WotAffordanceKind.Action, name, action, eventName, endpoint);
                m_forms.Add(form);
                var channel = new FakeWotBindingChannel(form);
                Invokes.Add(name, channel);
                Nodes.ChannelFactory.SetChannel(form, channel);
                m_declarations.Add(new WotProjectedAffordance(
                    WotAffordanceKind.Action, name, "/actions/" + name, method.NodeId.ToString(),
                    notifier.NodeId.ToString(), action, eventName));
                return method;
            }

            private static WotCompiledForm Form(
                WotAffordanceKind kind, string name, string target, string? owner, string endpoint)
            {
                bool isEvent = kind == WotAffordanceKind.Event;
                WoTBindingCapabilityEnum operation = isEvent
                    ? WoTBindingCapabilityEnum.SubscribeEvent : WoTBindingCapabilityEnum.InvokeAction;
                string op = isEvent ? "subscribeevent" : "invokeaction";
                ImmutableDictionary<string, string> addressing = owner is null
                    ? ImmutableDictionary<string, string>.Empty
                    : ImmutableDictionary<string, string>.Empty.Add("componentOf", "nsu=urn:source;s=" + owner);
                return new WotCompiledForm(
                    new WotBindingIdentity("test", "1", "urn:test"), kind, name,
                    (isEvent ? "/events/" : "/actions/") + name + "/forms/0", operation, op,
                    new WotEndpointDescriptor("test", null, -1, endpoint),
                    new WotAddressingDescriptor(target, addressing),
                    new WotOperationDescriptor(operation, op, isEvent ? "Monitor" : "Call"),
                    new WotPayloadDescriptor("application/json", "json"), [], true, null,
                    isEvent ? s_selection : null, null);
            }

            private readonly int m_maxRoutes;
            private readonly List<WotCompiledForm> m_forms = [];
            private readonly List<WotProjectedAffordance> m_declarations = [];
        }

        private sealed class RecordingPublisher : IWotProjectionEventPublisher
        {
            public List<NodeId> Roots { get; } = [];

            public void Register(
                INodeManagerBuilder builder,
                BaseObjectState notifier,
                Func<CancellationToken, IAsyncEnumerable<BaseEventState>> source,
                bool registerAsRootNotifier)
            {
                m_sources.Add((builder, notifier.NodeId), source);
                if (registerAsRootNotifier)
                {
                    Roots.Add(notifier.NodeId);
                }
            }

            public IAsyncEnumerator<BaseEventState> Open(NodeId notifier, CancellationToken cancellationToken = default)
            {
                return OpenStream(notifier, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }

            public IAsyncEnumerable<BaseEventState> OpenStream(
                NodeId notifier, CancellationToken cancellationToken = default)
            {
                foreach (KeyValuePair<
                    (INodeManagerBuilder Builder, NodeId Notifier),
                    Func<CancellationToken, IAsyncEnumerable<BaseEventState>>> entry in m_sources)
                {
                    if (entry.Key.Notifier == notifier)
                    {
                        return entry.Value(cancellationToken);
                    }
                }
                throw new InvalidOperationException("No notifier was registered.");
            }

            private readonly Dictionary<
                (INodeManagerBuilder Builder, NodeId Notifier),
                Func<CancellationToken, IAsyncEnumerable<BaseEventState>>> m_sources = [];
        }

        private sealed class RecordingConditionFactory(WotProjectionBindingRuntimeTestHarness harness)
            : IWotProjectionConditionFactory
        {
            public Dictionary<string, ConditionState> Nodes { get; } = new(StringComparer.Ordinal);

            public ValueTask<ConditionState> CreateAsync(
                INodeManagerBuilder builder,
                BaseObjectState notifier,
                WotProjectedAffordance declaration,
                NodeId eventTypeId,
                CancellationToken cancellationToken = default)
            {
                var condition = new AcknowledgeableConditionState(notifier)
                {
                    NodeId = new NodeId(declaration.Name + "Condition", harness.Ns),
                    BrowseName = new QualifiedName(declaration.Name, harness.Ns),
                    TypeDefinitionId = eventTypeId
                };
                foreach (string name in new[] { "Acknowledge", "Confirm" })
                {
                    var method = new AddCommentMethodState(condition)
                    {
                        NodeId = new NodeId(declaration.Name + "Condition" + name, harness.Ns),
                        BrowseName = QualifiedName.From(name),
                        Executable = true,
                        UserExecutable = true,
                        OnCall = (_, _, _, _, _) => new ServiceResult(StatusCodes.BadUnexpectedError)
                    };
                    method.CreateOrReplaceInputArguments(builder.Context, null).Value =
                    [
                        new Argument
                        {
                            Name = "EventId", DataType = Ua.DataTypeIds.ByteString, ValueRank = ValueRanks.Scalar
                        },
                        new Argument
                        {
                            Name = "Comment", DataType = Ua.DataTypeIds.LocalizedText, ValueRank = ValueRanks.Scalar
                        }
                    ];
                    if (name == "Acknowledge")
                    {
                        condition.Acknowledge = method;
                    }
                    else
                    {
                        condition.Confirm = method;
                    }
                }
                notifier.AddChild(condition);
                Nodes[declaration.Name] = condition;
                return new ValueTask<ConditionState>(condition);
            }
        }

        private sealed class EventChannel
        {
            public EventChannel(WotCompiledForm form)
            {
                Channel = new FakeWotBindingChannel(form)
                {
                    OnSubscribeEvent = (listener, _) =>
                    {
                        m_listener = listener;
                        Started.TrySetResult(true);
                        return new ValueTask<IWotSubscription>(new EventSubscription(form, this));
                    }
                };
            }

            public FakeWotBindingChannel Channel { get; }

            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Stopped { get; private set; }

            public void Push(WotNotification notification)
            {
                (m_listener ?? throw new InvalidOperationException("No subscription."))(notification);
            }

            private sealed class EventSubscription(WotCompiledForm form, EventChannel owner) : IWotSubscription
            {
                public WotCompiledForm Form { get; } = form;

                public ValueTask DisposeAsync()
                {
                    owner.Stopped++;
                    owner.m_listener = null;
                    return default;
                }
            }

            private Action<WotNotification>? m_listener;
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
        private static readonly WotEventSelection s_selection = new(
            [
                .. WotEventSelection.Default.Clauses,
                new WotResolvedEventSelectClause("i=2782", string.Empty),
                new WotResolvedEventSelectClause("i=2782", "BranchId"),
                new WotResolvedEventSelectClause("i=2881", "AckedState/Id"),
                new WotResolvedEventSelectClause("i=2881", "ConfirmedState/Id"),
                new WotResolvedEventSelectClause("i=2915", "ActiveState/Id")
            ],
            WotEventSelectionOrigin.Standard);
    }
}
