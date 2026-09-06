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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotProjectedEventRuntimeTests
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
        public async Task DuplicateOccurrencesAreNotDeliveredAndEvictedOccurrencesCannotBeAcknowledged()
        {
            var h = new EventHarness(maxRoutes: 1);
            var (form, acknowledge, _) = h.AddAlarm("a");
            var upstream = new EventChannel(form);
            h.Nodes.ChannelFactory.SetChannel(form, upstream.Channel);
            IAsyncDisposable runtime = await h.WireAsync().ConfigureAwait(false);
            await using var runtimeOwner = runtime.ConfigureAwait(false);
            IAsyncEnumerator<BaseEventState> events = h.Publisher.Open(h.Nodes.Root.NodeId);
            await using var eventsOwner = events.ConfigureAwait(false);
            Task<bool> pending = events.MoveNextAsync().AsTask();
            await upstream.Started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            WotNotification first = Notification("a", new ByteString(new byte[] { 1 }));
            upstream.Push(first);
            Assert.That(await pending.WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            ByteString retiredId = events.Current.EventId!.Value;

            upstream.Push(first);
            upstream.Push(Notification("a", new ByteString(new byte[] { 2 }), acked: true));
            Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(s_timeout).ConfigureAwait(false), Is.True);
            Assert.That(Read(events.Current, "AckedState", "Id").TryGetValue(out bool acked) && acked, Is.True);
            ServiceResult result = await CallAsync(h, acknowledge, [new Variant(retiredId)]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
            Assert.That(h.Invokes["aAcknowledge"].InvokeCount, Is.Zero);
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

            public IAsyncEnumerator<BaseEventState> Open(NodeId notifier)
            {
                foreach (var entry in m_sources)
                {
                    if (entry.Key.Notifier == notifier)
                    {
                        return entry.Value(CancellationToken.None).GetAsyncEnumerator();
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
