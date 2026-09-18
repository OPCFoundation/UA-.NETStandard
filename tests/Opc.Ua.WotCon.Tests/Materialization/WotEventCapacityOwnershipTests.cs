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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotEventCapacityOwnershipTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task RetainedMainAndBranchCapacityKeepsTheFirstOccurrenceOwnedAsync(string mode)
        {
            await using var h = new CapacityHarness();
            WotProjectedEventBinding binding = h.CreateBinding(mode);
            WotNotification first = h.Notification(1);
            BaseEventState? accepted = await binding.ProjectAsync(first, CancellationToken.None).ConfigureAwait(false);
            Assert.That(accepted, Is.Not.Null);
            ByteString localId = accepted!.EventId!.Value;
            ConditionState main = h.Conditions[0];
            (BaseEventState? branch, StatusCode capacity) = await TryProjectAsync(
                binding, h.Notification(2, branch: true)).ConfigureAwait(false);
            bool stillRoutable = binding.TryResolveOwnEventId(localId, out ByteString sourceId);
            (BaseEventState? changed, StatusCode collision) = await TryProjectAsync(
                binding, h.Notification(1, severity: 999)).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(capacity, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(branch, Is.Null, "A full generation must not publish the second retained branch.");
                Assert.That(stillRoutable, Is.True, "Capacity rejection cannot expire the accepted route.");
                Assert.That(sourceId, Is.EqualTo(first.CapturedEvent!.EventId));
                Assert.That(changed, Is.Null, "The altered E1 cannot become a new occurrence after capacity pressure.");
                Assert.That(collision, Is.AnyOf(
                    StatusCodes.BadSecurityChecksFailed,
                    StatusCodes.BadTooManyOperations));
                Assert.That(main.EventId!.Value, Is.EqualTo(localId));
                Assert.That(main.Severity!.Value, Is.EqualTo((ushort)412));
                Assert.That(main.Retain!.Value, Is.True);
                Assert.That(h.Conditions, Has.Count.EqualTo(1));
            }
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task OrdinaryCapacityCannotExpireIdentityIntoReuseAsync(string mode)
        {
            await using var h = new CapacityHarness();
            WotProjectedEventBinding binding = h.CreateBinding(mode, isCondition: false);
            WotNotification first = h.Notification(1, isCondition: false);
            BaseEventState? accepted = await binding.ProjectAsync(first, CancellationToken.None).ConfigureAwait(false);
            Assert.That(accepted, Is.Not.Null);
            ByteString localId = accepted!.EventId!.Value;
            BaseEventState? replay = await binding.ProjectAsync(first, CancellationToken.None).ConfigureAwait(false);
            (BaseEventState? second, StatusCode capacity) = await TryProjectAsync(
                binding, h.Notification(2, isCondition: false)).ConfigureAwait(false);
            bool owned = binding.TryResolveOwnEventId(localId, out _);
            (BaseEventState? reused, StatusCode collision) = await TryProjectAsync(
                binding, h.Notification(1, severity: 999, isCondition: false)).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(replay, Is.Null, "An exact replay consumes no additional occurrence slot.");
                Assert.That(second, Is.Null);
                Assert.That(capacity, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(owned, Is.True);
                Assert.That(reused, Is.Null);
                Assert.That(collision, Is.AnyOf(
                    StatusCodes.BadSecurityChecksFailed,
                    StatusCodes.BadTooManyOperations));
            }

            h.Remove(binding);
            Assert.That(binding.TryResolveOwnEventId(localId, out _), Is.False);
            WotProjectedEventBinding replacement = h.CreateBinding(mode, isCondition: false);
            BaseEventState? next = await replacement.ProjectAsync(
                h.Notification(2, isCondition: false), CancellationToken.None).ConfigureAwait(false);
            Assert.That(next, Is.Not.Null, "Disposing the owner permits an explicit replacement generation.");
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task FailedConditionCreationCanRetryWithoutLeakingCapacityAsync(string mode)
        {
            await using var h = new CapacityHarness();
            int attempts = 0;
            WotProjectedEventBinding binding = h.CreateBinding(mode, createCondition: (id, _) =>
            {
                if (++attempts == 1)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable, "Expected factory rejection.");
                }
                return new ValueTask<ConditionState>(h.CreateCondition(id));
            });
            WotNotification first = h.Notification(1);
            (_, StatusCode failure) = await TryProjectAsync(binding, first).ConfigureAwait(false);
            (BaseEventState? retry, StatusCode status) = await TryProjectAsync(binding, first).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(status, Is.EqualTo(StatusCodes.Good), "A failed factory cannot keep a poisoned slot.");
                Assert.That(retry, Is.Not.Null);
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(h.Conditions, Has.Count.EqualTo(1));
            }
            (_, StatusCode capacity) = await TryProjectAsync(binding, h.Notification(2)).ConfigureAwait(false);
            Assert.That(capacity, Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task CancelledConditionCreationReleasesOnlyUncommittedOwnershipAsync(string mode)
        {
            await using var h = new CapacityHarness();
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            WotProjectedEventBinding binding = h.CreateBinding(mode, createCondition: async (id, ct) =>
            {
                if (++attempts == 1)
                {
                    started.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                return h.CreateCondition(id);
            });
            Task<BaseEventState?> pending = binding.ProjectAsync(h.Notification(1), cancellation.Token).AsTask();
            await started.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            cancellation.Cancel();
            await Assert.ThatAsync(() => pending.WaitAsync(s_timeout),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            WotProjectedEventBinding other = h.CreateBinding(mode);
            BaseEventState? otherEvent = await other.ProjectAsync(
                h.Notification(1), CancellationToken.None).ConfigureAwait(false);
            Assert.That(otherEvent, Is.Not.Null, "The cancelled admission must not reserve the rejected EventId.");
            h.Remove(other);
            (BaseEventState? retry, StatusCode status) = await TryProjectAsync(
                binding, h.Notification(2)).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(status, Is.EqualTo(StatusCodes.Good));
                Assert.That(retry, Is.Not.Null);
                Assert.That(attempts, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task TransparentReservationPrecedesFactoryAndRollbackPreservesOtherOwnersAsync()
        {
            await using var h = new CapacityHarness();
            const string Mode = "transparent-forwarding";
            WotNotification occurrence = h.Notification(1);
            WotNotification conflicting = h.Notification(1, severity: 999);
            WotProjectedEventBinding contender = h.CreateBinding(Mode);
            bool reservedBeforeCreation = false;
            WotProjectedEventBinding owner = h.CreateBinding(Mode, createCondition: (id, _) =>
            {
                try
                {
                    h.Registry.ValidateTransparentSource(contender, conflicting.CapturedEvent!);
                }
                catch (ServiceResultException exception) when (
                    exception.StatusCode == StatusCodes.BadSecurityChecksFailed)
                {
                    reservedBeforeCreation = true;
                }
                return new ValueTask<ConditionState>(h.CreateCondition(id));
            });
            BaseEventState? accepted = await owner.ProjectAsync(occurrence, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(accepted, Is.Not.Null);
            WotProjectedEventBinding failedOwner = h.CreateBinding(Mode, createCondition: (_, _) =>
                throw new ServiceResultException(
                    StatusCodes.BadResourceUnavailable, "Expected second-owner rejection."));
            (_, StatusCode failure) = await TryProjectAsync(failedOwner, occurrence).ConfigureAwait(false);
            ServiceResultException? stillOwned = Assert.Throws<ServiceResultException>(() =>
                h.Registry.ValidateTransparentSource(contender, conflicting.CapturedEvent!));
            h.Remove(owner);
            Assert.DoesNotThrow(() => h.Registry.ValidateTransparentSource(contender, conflicting.CapturedEvent!),
                "A rolled-back admission must not outlive the last accepted owner.");
            BaseEventState? reclaimed = await contender.ProjectAsync(
                conflicting, CancellationToken.None).ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reservedBeforeCreation, Is.True);
                Assert.That(failure, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(stillOwned!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(reclaimed, Is.Not.Null);
            }
        }

        private static async Task<(BaseEventState? Event, StatusCode Status)> TryProjectAsync(
            WotProjectedEventBinding binding, WotNotification notification)
        {
            try
            {
                BaseEventState? result = await binding.ProjectAsync(notification, CancellationToken.None)
                    .ConfigureAwait(false);
                return (result, StatusCodes.Good);
            }
            catch (ServiceResultException exception)
            {
                return (null, exception.StatusCode);
            }
        }

        private sealed class CapacityHarness : IAsyncDisposable
        {
            public CapacityHarness()
            {
                var session = new Mock<ISessionBinding>();
                ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
                context.NamespaceUris = new NamespaceTable([Ua.Namespaces.OpcUa, SourceNamespace]);
                session.SetupGet(value => value.MessageContext).Returns(context);
                session.SetupGet(value => value.SessionId).Returns(new NodeId("capacity-session", 0));
                session.SetupGet(value => value.IsCurrent).Returns(true);
                session.SetupGet(value => value.Endpoint).Returns(new EndpointDescription
                {
                    Server = new ApplicationDescription { ApplicationUri = "urn:wot:capacity:source" },
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = Uuid.NewUuid().ToByteString()
                });
                m_session = session.Object;
                m_source = new WotEventSource(m_session);
                m_nodes.Builder.Context.NamespaceUris.GetIndexOrAppend(SourceNamespace);
            }

            public WotProjectedEventRouteRegistry Registry { get; } = new();

            public List<ConditionState> Conditions { get; } = [];

            public WotProjectedEventBinding CreateBinding(
                string mode,
                bool isCondition = true,
                Func<NodeId, CancellationToken, ValueTask<ConditionState>>? createCondition = null)
            {
                bool transparent = mode == "transparent-forwarding";
                var form = new WotCompiledForm(
                    new WotBindingIdentity("opc.opcua", "10101", "urn:opcfoundation:wot:binding:opcua"),
                    Bindings.WotAffordanceKind.Event, "event", "/events/event/forms/0",
                    WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                    new WotEndpointDescriptor("opc.tcp", "source", 4840, "opc.tcp://source:4840"),
                    new WotAddressingDescriptor("i=2253"),
                    new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                    new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                    null, WotEventSelection.Default, null);
                var slot = new WotBindingChannelSlot(form, Mock.Of<IWotBindingChannelFactory>());
                var eventSource = new WotProjectedEventSource(form, slot);
                ushort ns = transparent
                    ? checked((ushort)m_nodes.Builder.Context.NamespaceUris.GetIndex(SourceNamespace)) : m_nodes.Ns;
                var binding = new WotProjectedEventBinding(
                    m_nodes.Builder.Context, eventSource, m_nodes.Root, new NodeId("EventType", ns), null,
                    ExpandedNodeId.Null, 1, TimeProvider.System, "urn:wot:capacity:document", "/events/event", Registry,
                    transparent
                        ? WoTEventIdentityModeEnum.TransparentForwarding : WoTEventIdentityModeEnum.LocalReEmission,
                    isCondition, createCondition ?? ((id, _) => new ValueTask<ConditionState>(CreateCondition(id))));
                Registry.Add(binding);
                m_bindings.Add(binding);
                m_sources.Add(eventSource);
                m_slots.Add(slot);
                return binding;
            }

            public ConditionState CreateCondition(NodeId nodeId)
            {
                var condition = new ConditionState(null);
                condition.Create(m_nodes.Builder.Context, nodeId, new QualifiedName("Condition", nodeId.NamespaceIndex),
                    new LocalizedText("Condition"), assignNodeIds: false);
                Conditions.Add(condition);
                return condition;
            }

            public WotNotification Notification(
                byte id, bool branch = false, ushort severity = 412, bool isCondition = true)
            {
                ArrayOf<WotResolvedEventSelectClause> clauses = isCondition
                    ? WotCapturedEvent.RequiredSelectClauses : WotEventSelectClauses.Default;
                ArrayOf<Variant> values = clauses.ConvertAll(clause => clause.BrowsePath switch
                {
                    "EventId" => new Variant(ByteString.From(new byte[] { 0xD3, 0xC2, id })),
                    "EventType" => new Variant(new NodeId("EventType", 1)),
                    "SourceNode" => new Variant(new NodeId("Pump", 1)),
                    "" => new Variant(new NodeId("Condition", 1)),
                    "BranchId" => new Variant(branch ? new NodeId("Branch", 1) : NodeId.Null),
                    "ConditionClassId" => new Variant(Ua.ObjectTypeIds.ProcessConditionClassType),
                    "SourceName" or "ConditionName" or "ClientUserId" => new Variant("source"),
                    "Message" or "ConditionClassName" or "EnabledState" or "Comment" =>
                        new Variant(new LocalizedText("Retained source Condition")),
                    "Severity" => new Variant(severity),
                    "LastSeverity" => new Variant((ushort)200),
                    "Time" or "ReceiveTime" or "Quality/SourceTimestamp" or
                        "LastSeverity/SourceTimestamp" or "Comment/SourceTimestamp" => new Variant(SourceTime),
                    "Retain" or "EnabledState/Id" => new Variant(true),
                    "Quality" => new Variant(StatusCodes.Good),
                    _ => throw new ArgumentOutOfRangeException(nameof(clause))
                });
                var fields = new Dictionary<string, WotEventData>(StringComparer.Ordinal);
                for (int index = 0; index < WotEventSelection.Default.Clauses.Count; index++)
                {
                    fields.Add(clauses[index].BrowsePath, new WotEventData(new DataValue(values[index])));
                }
                return new WotNotification(new DataValue(Variant.Null), null,
                    new WotEventData(fields), [Ua.Namespaces.OpcUa, SourceNamespace])
                    .WithCapturedEvent(WotCapturedEvent.Capture(m_source, clauses, values));
            }

            public void Remove(WotProjectedEventBinding binding)
            {
                binding.Clear();
                Registry.Remove(binding);
            }

            public async ValueTask DisposeAsync()
            {
                foreach (WotProjectedEventBinding binding in m_bindings)
                {
                    Remove(binding);
                }
                foreach (WotProjectedEventSource source in m_sources)
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
                foreach (WotBindingChannelSlot slot in m_slots)
                {
                    await slot.DisposeAsync().ConfigureAwait(false);
                }
                m_session.Dispose();
            }

            private readonly WotProjectionBindingRuntimeTestHarness m_nodes = new();
            private readonly ISessionBinding m_session;
            private readonly WotEventSource m_source;
            private readonly List<WotProjectedEventBinding> m_bindings = [];
            private readonly List<WotProjectedEventSource> m_sources = [];
            private readonly List<WotBindingChannelSlot> m_slots = [];
        }

        private const string SourceNamespace = "urn:wot:capacity:source";
        private static readonly DateTimeUtc SourceTime = new(2026, 8, 1, 1, 2, 3);
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);
    }
}
