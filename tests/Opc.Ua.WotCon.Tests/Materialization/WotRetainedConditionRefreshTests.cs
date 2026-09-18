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
    public sealed class WotRetainedConditionRefreshTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task RetainedPropertyUpdateReusesIdentityAndFiniteCapacityAsync(string mode)
        {
            await using var h = new RefreshHarness(mode);
            BaseEventState? first = await h.ProjectAsync().ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            ByteString eventId = first!.EventId!.Value;
            h.Alarm.HighLimit!.Value = 110;
            h.Alarm.ReceiveTime!.Value = s_time + TimeSpan.FromSeconds(1);
            BaseEventState? refresh = await h.ProjectAsync().ConfigureAwait(false);
            Assert.That(refresh, Is.Not.Null);
            Assert.That(refresh!.EventId!.Value, Is.EqualTo(eventId));
            Assert.That(Read(refresh, h.Context, "HighLimit"), Is.EqualTo(new Variant(110d)));
            Assert.That(h.Conditions, Has.Count.EqualTo(1));
            Assert.That(h.Conditions[0].HighLimit!.Value, Is.EqualTo(110d));
            Assert.That(h.Binding.TryResolveOwnEventId(eventId, out ByteString original), Is.True);
            Assert.That(original, Is.EqualTo(s_eventId));
            Assert.That(await h.ProjectAsync().ConfigureAwait(false), Is.Null);
            h.Alarm.EventId!.Value = ByteString.From(new byte[] { 0xD3, 0xC4, 0x12 });
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await h.ProjectAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(h.Conditions[0].EventId!.Value, Is.EqualTo(eventId));
            Assert.That(h.Conditions[0].HighLimit!.Value, Is.EqualTo(110d));
        }

        [TestCase("local-re-emission", "Time")]
        [TestCase("transparent-forwarding", "Time")]
        [TestCase("local-re-emission", "Severity")]
        [TestCase("transparent-forwarding", "Severity")]
        [TestCase("local-re-emission", "EnabledState")]
        [TestCase("transparent-forwarding", "EnabledState")]
        [TestCase("local-re-emission", "ActiveState")]
        [TestCase("transparent-forwarding", "ActiveState")]
        [TestCase("local-re-emission", "AckedState")]
        [TestCase("transparent-forwarding", "AckedState")]
        [TestCase("local-re-emission", "Retain")]
        [TestCase("transparent-forwarding", "Retain")]
        [TestCase("local-re-emission", "BranchId")]
        [TestCase("transparent-forwarding", "BranchId")]
        [TestCase("local-re-emission", "ConditionId")]
        [TestCase("transparent-forwarding", "ConditionId")]
        [TestCase("local-re-emission", "SourceNode")]
        [TestCase("transparent-forwarding", "SourceNode")]
        [TestCase("local-re-emission", "ForeignOrigin")]
        [TestCase("transparent-forwarding", "ForeignOrigin")]
        [TestCase("local-re-emission", "NamespaceHighLimit")]
        [TestCase("transparent-forwarding", "NamespaceHighLimit")]
        [TestCase("local-re-emission", "NonLimitType")]
        [TestCase("transparent-forwarding", "NonLimitType")]
        [TestCase("local-re-emission", "NotRetained")]
        [TestCase("transparent-forwarding", "NotRetained")]
        public async Task ChangedStateOrUnprovenPropertyCannotMasqueradeAsRetainedRefreshAsync(
            string mode, string change)
        {
            await using var h = new RefreshHarness(
                mode, limitType: change != "NonLimitType", qualifiedProperty: change == "NamespaceHighLimit");
            if (change == "NotRetained")
            {
                h.Alarm.Retain!.Value = false;
            }
            BaseEventState? first = await h.ProjectAsync().ConfigureAwait(false);
            Assert.That(first, Is.Not.Null);
            ByteString eventId = first!.EventId!.Value;
            switch (change)
            {
                case "Time":
                    h.Alarm.Time!.Value = s_time + TimeSpan.FromSeconds(1);
                    break;
                case "Severity":
                    h.Alarm.Severity!.Value = 999;
                    break;
                case "EnabledState":
                    h.Alarm.EnabledState!.Id!.Value = false;
                    break;
                case "ActiveState":
                    h.Alarm.ActiveState!.Id!.Value = false;
                    break;
                case "AckedState":
                    h.Alarm.AckedState!.Id!.Value = true;
                    break;
                case "Retain":
                    h.Alarm.Retain!.Value = false;
                    break;
                case "BranchId":
                    h.Alarm.BranchId!.Value = new NodeId("other-branch", 1);
                    break;
                case "ConditionId":
                    h.Alarm.NodeId = new NodeId("other-condition", 1);
                    break;
                case "SourceNode":
                    h.Alarm.SourceNode!.Value = new NodeId("other-source", 1);
                    break;
                case "ForeignOrigin":
                    h.ReplaceSource();
                    break;
                case "NamespaceHighLimit":
                    h.Lookalike.Value = 110;
                    break;
                case "NonLimitType":
                case "NotRetained":
                    h.Alarm.HighLimit!.Value = 110;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await h.ProjectAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(h.Conditions, Has.Count.EqualTo(1));
                Assert.That(h.Conditions[0].EventId!.Value, Is.EqualTo(eventId));
                Assert.That(h.Conditions[0].Time!.Value, Is.EqualTo(s_time));
                Assert.That(h.Conditions[0].Severity!.Value, Is.EqualTo((ushort)412));
                Assert.That(h.Conditions[0].EnabledState!.Id!.Value, Is.True);
                Assert.That(h.Conditions[0].ActiveState!.Id!.Value, Is.True);
                Assert.That(h.Conditions[0].AckedState!.Id!.Value, Is.False);
                Assert.That(h.Conditions[0].HighLimit!.Value, Is.EqualTo(100d));
                Assert.That(h.Conditions[0].BranchId!.Value.IsNull, Is.True);
                Assert.That(h.Conditions[0].Retain!.Value, Is.EqualTo(change != "NotRetained"));
            }
        }

        internal static LimitAlarmState CreateAlarm(
            ISystemContext context, NodeId typeId, NodeId conditionId, ByteString eventId, DateTimeUtc time)
        {
            var alarm = new LimitAlarmState(null);
            alarm.Create(context, conditionId, new QualifiedName("retainedLimitAlarm", conditionId.NamespaceIndex),
                new LocalizedText("Retained limit alarm"), assignNodeIds: false);
            alarm.TypeDefinitionId = typeId;
            alarm.EventId!.Value = eventId;
            alarm.EventType!.Value = typeId;
            alarm.SourceNode!.Value = new NodeId("PumpA", conditionId.NamespaceIndex);
            alarm.SourceName!.Value = "PumpA";
            alarm.Time!.Value = time;
            alarm.ReceiveTime!.Value = time;
            alarm.Message!.Value = new LocalizedText("D3 Condition occurrence");
            alarm.Severity!.Value = 412;
            alarm.ConditionName!.Value = "ConditionA";
            alarm.ConditionClassId!.Value = Ua.ObjectTypeIds.ProcessConditionClassType;
            alarm.ConditionClassName!.Value = new LocalizedText("Process");
            alarm.BranchId!.Value = NodeId.Null;
            alarm.Retain!.Value = true;
            alarm.EnabledState!.Value = new LocalizedText("Enabled");
            alarm.EnabledState.Id!.Value = true;
            alarm.ActiveState!.Id!.Value = true;
            alarm.AckedState!.Id!.Value = false;
            alarm.Quality!.Value = StatusCodes.Good;
            alarm.Quality.SourceTimestamp!.Value = time;
            alarm.LastSeverity!.Value = 200;
            alarm.LastSeverity.SourceTimestamp!.Value = time;
            alarm.Comment!.Value = new LocalizedText("Unchanged");
            alarm.Comment.SourceTimestamp!.Value = time;
            alarm.ClientUserId!.Value = "source-operator";
            alarm.HighLimit ??= PropertyState<double>.With<VariantBuilder>(alarm);
            alarm.HighLimit.BrowseName = QualifiedName.From(Ua.BrowseNames.HighLimit);
            alarm.HighLimit.Value = 100;
            return alarm;
        }

        private static Variant Read(BaseEventState state, SystemContext context, string path)
        {
            return state.GetAttributeValue(
                new FilterContext(context.NamespaceUris, context.TypeTable, context.Telemetry),
                NodeId.Null, [QualifiedName.From(path)], Attributes.Value, default);
        }

        private sealed class RefreshHarness : IAsyncDisposable
        {
            public RefreshHarness(string mode, bool limitType = true, bool qualifiedProperty = false)
            {
                ServiceMessageContext messageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
                messageContext.NamespaceUris = new NamespaceTable(
                    [Ua.Namespaces.OpcUa, SourceNamespace, LocalNamespace]);
                var types = new TypeTable(messageContext.NamespaceUris);
                types.AddSubtype(Ua.ObjectTypeIds.BaseEventType, NodeId.Null);
                types.AddSubtype(Ua.ObjectTypeIds.ConditionType, Ua.ObjectTypeIds.BaseEventType);
                types.AddSubtype(Ua.ObjectTypeIds.AcknowledgeableConditionType, Ua.ObjectTypeIds.ConditionType);
                types.AddSubtype(Ua.ObjectTypeIds.AlarmConditionType, Ua.ObjectTypeIds.AcknowledgeableConditionType);
                types.AddSubtype(Ua.ObjectTypeIds.LimitAlarmType, Ua.ObjectTypeIds.AlarmConditionType);
                types.AddSubtype(s_sourceType, limitType
                    ? Ua.ObjectTypeIds.LimitAlarmType : Ua.ObjectTypeIds.ConditionType);
                types.AddSubtype(s_localType, s_sourceType);
                Context = new SystemContext(NUnitTelemetryContext.Create())
                {
                    NamespaceUris = messageContext.NamespaceUris,
                    ServerUris = messageContext.ServerUris,
                    EncodeableFactory = messageContext.Factory,
                    TypeTable = types
                };
                Alarm = CreateAlarm(Context, s_sourceType, new NodeId("retainedLimitAlarm", 1), s_eventId, s_time);
                Lookalike = PropertyState<double>.With<VariantBuilder>(Alarm, 100d);
                Lookalike.BrowseName = new QualifiedName(Ua.BrowseNames.HighLimit, 1);
                Alarm.AddChild(Lookalike);
                m_messageContext = messageContext;
                ReplaceSource();
                var selection = new WotEventSelection(
                    [
                        new WotResolvedEventSelectClause("i=2782", "ConditionName"),
                        new WotResolvedEventSelectClause(s_sourceTypeText,
                            qualifiedProperty ? "nsu=" + SourceNamespace + ";HighLimit" : "HighLimit"),
                        new WotResolvedEventSelectClause(s_sourceTypeText, "ActiveState/Id"),
                        new WotResolvedEventSelectClause(s_sourceTypeText, "AckedState/Id")
                    ],
                    WotEventSelectionOrigin.Standard);
                var form = new WotCompiledForm(
                    new WotBindingIdentity("opc.opcua", "10101", "urn:opcfoundation:wot:binding:opcua"),
                    Bindings.WotAffordanceKind.Event, "alarm", "/events/alarm/forms/0",
                    WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                    new WotEndpointDescriptor("opc.tcp", "source", 4840, "opc.tcp://source:4840"),
                    new WotAddressingDescriptor("i=2253"),
                    new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                    new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                    null, selection, null);
                m_slot = new WotBindingChannelSlot(form, Mock.Of<IWotBindingChannelFactory>());
                m_eventSource = new WotProjectedEventSource(form, m_slot);
                var notifier = new BaseObjectState(null) { NodeId = new NodeId("Owner", 2) };
                bool transparent = mode == "transparent-forwarding";
                Binding = new WotProjectedEventBinding(Context, m_eventSource, notifier,
                    transparent ? s_sourceType : s_localType, null, ExpandedNodeId.Null, 1, TimeProvider.System,
                    "urn:wot:refresh:document", "/events/alarm", m_registry,
                    transparent
                        ? WoTEventIdentityModeEnum.TransparentForwarding : WoTEventIdentityModeEnum.LocalReEmission,
                    isCondition: true, createCondition: (id, _) =>
                    {
                        LimitAlarmState condition = CreateAlarm(
                            Context, transparent ? s_sourceType : s_localType, id, default, s_time);
                        Conditions.Add(condition);
                        return new ValueTask<ConditionState>(condition);
                    });
                m_registry.Add(Binding);
            }

            public SystemContext Context { get; }

            public LimitAlarmState Alarm { get; }

            public PropertyState<double> Lookalike { get; }

            public WotProjectedEventBinding Binding { get; }

            public List<LimitAlarmState> Conditions { get; } = [];

            public ValueTask<BaseEventState?> ProjectAsync()
            {
                var filter = new FilterContext(Context.NamespaceUris, Context.TypeTable, Context.Telemetry);
                Variant ReadClause(WotResolvedEventSelectClause clause)
                {
                    ArrayOf<QualifiedName> path = clause.PathElements.ConvertAll(
                        element => WotBindingValueMapper.ResolveBrowseName(element, Context.NamespaceUris));
                    if (path.Count == 1 && path[0] == Lookalike.BrowseName)
                    {
                        return new Variant(Lookalike.Value);
                    }
                    return Alarm.GetAttributeValue(filter, NodeId.Null, path,
                        clause.IsConditionIdSelection ? Attributes.NodeId : Attributes.Value, default);
                }
                WotCapturedEvent captured = WotCapturedEvent.Capture(m_source,
                    WotCapturedEvent.RequiredSelectClauses,
                    WotCapturedEvent.RequiredSelectClauses.ConvertAll(ReadClause));
                var data = new WotEventDataBuilder();
                WotEventSelection selection = m_eventSource.Form.EventSelection!;
                ArrayOf<ArrayOf<string>> paths = WotEventSelectClauses.GetMaterializedMemberPaths(selection.Clauses);
                for (int index = 0; index < paths.Count; index++)
                {
                    Assert.That(data.Add(paths[index], new DataValue(ReadClause(selection.Clauses[index]),
                        StatusCodes.Good, Alarm.Time!.Value, Alarm.ReceiveTime!.Value)), Is.True);
                }
                return Binding.ProjectAsync(new WotNotification(new DataValue(Variant.Null), null, data.Build())
                    .WithContext(m_messageContext).WithCapturedEvent(captured), CancellationToken.None);
            }

            public void ReplaceSource()
            {
                var session = new Mock<ISessionBinding>();
                session.SetupGet(value => value.MessageContext).Returns(m_messageContext);
                session.SetupGet(value => value.SessionId).Returns(new NodeId("session", 0));
                session.SetupGet(value => value.IsCurrent).Returns(true);
                session.SetupGet(value => value.Endpoint).Returns(new EndpointDescription
                {
                    Server = new ApplicationDescription { ApplicationUri = "urn:wot:refresh:source" },
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = Uuid.NewUuid().ToByteString()
                });
                m_sessions.Add(session.Object);
                m_source = new WotEventSource(session.Object);
            }

            public async ValueTask DisposeAsync()
            {
                Binding.Clear();
                m_registry.Remove(Binding);
                await m_eventSource.DisposeAsync().ConfigureAwait(false);
                await m_slot.DisposeAsync().ConfigureAwait(false);
                foreach (ISessionBinding session in m_sessions)
                {
                    session.Dispose();
                }
            }

            private readonly ServiceMessageContext m_messageContext;
            private readonly WotBindingChannelSlot m_slot;
            private readonly WotProjectedEventSource m_eventSource;
            private readonly WotProjectedEventRouteRegistry m_registry = new();
            private readonly List<ISessionBinding> m_sessions = [];
            private WotEventSource m_source = null!;
        }

        private const string SourceNamespace = "urn:wot:retained-refresh:source";
        private const string LocalNamespace = "urn:wot:retained-refresh:local";
        private static readonly NodeId s_sourceType = new("LimitEventType", 1);
        private static readonly NodeId s_localType = new("LimitEventType", 2);
        private static readonly string s_sourceTypeText =
            new ExpandedNodeId("LimitEventType", SourceNamespace).ToString();
        private static readonly ByteString s_eventId = ByteString.From(new byte[] { 0xD3, 0xC4, 0x11 });
        private static readonly DateTimeUtc s_time = new(2026, 9, 18, 15, 0, 0);
    }
}
