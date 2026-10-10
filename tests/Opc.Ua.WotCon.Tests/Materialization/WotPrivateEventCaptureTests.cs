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

#if NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    [TestFixture]
    public sealed class WotPrivateEventCaptureTests
    {
        [TestCase("BaseEvent", 8)]
        [TestCase("Condition", 23)]
        [TestCase("Acknowledgeable", 25)]
        [TestCase("Alarm", 29)]
        [TestCase("Limit", 29)]
        public void RequiredCaptureMatchesAdvertisedCoreType(string kind, int count)
        {
            NodeId coreType = kind switch
            {
                "BaseEvent" => Ua.ObjectTypeIds.BaseEventType,
                "Condition" => Ua.ObjectTypeIds.ConditionType,
                "Acknowledgeable" => Ua.ObjectTypeIds.AcknowledgeableConditionType,
                "Alarm" => Ua.ObjectTypeIds.AlarmConditionType,
                "Limit" => Ua.ObjectTypeIds.LimitAlarmType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            ArrayOf<WotResolvedEventSelectClause> clauses = WotCapturedEvent.GetRequiredSelectClauses(coreType);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(clauses.Count, Is.EqualTo(count));
                Assert.That(clauses.Contains(clause => clause.IsConditionIdSelection),
                    Is.EqualTo(kind != "BaseEvent"));
                Assert.That(clauses.Contains(clause => clause.BrowsePath == "AckedState/Id"),
                    Is.EqualTo(kind is "Acknowledgeable" or "Alarm" or "Limit"));
                Assert.That(clauses.Contains(clause => clause.BrowsePath == "ActiveState/Id"),
                    Is.EqualTo(kind is "Alarm" or "Limit"));
                Assert.That(clauses.Contains(clause => clause.BrowsePath == "InputNode"),
                    Is.EqualTo(kind is "Alarm" or "Limit"));
                Assert.That(clauses.Contains(clause => clause.BrowsePath == "SuppressedOrShelved"),
                    Is.EqualTo(kind is "Alarm" or "Limit"));
            }
            for (int index = 0; index < WotEventSelectClauses.Default.Count; index++)
            {
                Assert.That(clauses[index], Is.EqualTo(WotEventSelectClauses.Default[index]));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RequiredCaptureRejectsUnsupportedCoreTypes(bool custom)
        {
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                WotCapturedEvent.GetRequiredSelectClauses(custom ? new NodeId("Custom", 1) : NodeId.Null));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void RequiredCapturePreservesPublicPrefixAndDeduplicatesExactOperands()
        {
            ArrayOf<SimpleAttributeOperand> authored =
            [
                Operand(Ua.ObjectTypeIds.ConditionType, Attributes.Value, "ConditionName"),
                Operand(Ua.ObjectTypeIds.BaseEventType, Attributes.Value, "EventId"),
                Operand(Ua.ObjectTypeIds.BaseEventType, Attributes.Value, "EventId"),
                Operand(Ua.ObjectTypeIds.ConditionType, Attributes.NodeId),
                Operand(Ua.ObjectTypeIds.BaseEventType, Attributes.NodeId),
                Operand(Ua.ObjectTypeIds.ConditionType, Attributes.Value, "EventId"),
                new SimpleAttributeOperand
                {
                    TypeDefinitionId = new NodeId("VendorType", 1),
                    AttributeId = Attributes.Value,
                    BrowsePath = [new QualifiedName("EventId", 1)]
                },
                Operand(Ua.ObjectTypeIds.BaseEventType, Attributes.Value, "SourceNode"),
                Operand(Ua.ObjectTypeIds.ConditionType, Attributes.Value, "EnabledState"),
                Operand(Ua.ObjectTypeIds.ConditionType, Attributes.Value, "EnabledState", "Id")
            ];
            var filter = new EventFilter { SelectClauses = authored };
            ArrayOf<int> indexes = OpcUaWotBindingChannel.AppendRequiredEventFields(
                filter, WotCapturedEvent.RequiredSelectClauses);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(authored.Count, Is.EqualTo(10));
                Assert.That(WotCapturedEvent.RequiredSelectClauses.Count, Is.EqualTo(23));
                Assert.That(filter.SelectClauses.Count, Is.EqualTo(27));
                Assert.That(indexes[CoreIndex("EventId")], Is.EqualTo(1));
                Assert.That(indexes[CoreIndex(string.Empty)], Is.EqualTo(3));
                Assert.That(indexes[CoreIndex("ConditionName")], Is.Zero);
                Assert.That(indexes[CoreIndex("SourceNode")], Is.EqualTo(7));
                Assert.That(indexes[CoreIndex("EnabledState")], Is.EqualTo(8));
                Assert.That(indexes[CoreIndex("EnabledState/Id")], Is.EqualTo(9));
            }
            for (int index = 0; index < authored.Count; index++)
            {
                Assert.That(filter.SelectClauses[index], Is.SameAs(authored[index]));
            }
            for (int index = 0; index < indexes.Count; index++)
            {
                WotResolvedEventSelectClause clause = WotCapturedEvent.RequiredSelectClauses[index];
                SimpleAttributeOperand operand = filter.SelectClauses[indexes[index]];
                Assert.That(operand.TypeDefinitionId, Is.EqualTo(NodeId.Parse(clause.TypeDefinitionId)));
                Assert.That(operand.AttributeId,
                    Is.EqualTo(clause.IsConditionIdSelection ? Attributes.NodeId : Attributes.Value));
                Assert.That(operand.BrowsePath,
                    Is.EqualTo(clause.PathElements.ConvertAll(QualifiedName.From)));
            }
            Assert.That(OpcUaWotBindingChannel.AppendRequiredEventFields(
                filter, WotCapturedEvent.RequiredSelectClauses), Is.EqualTo(indexes));
            Assert.That(filter.SelectClauses.Count, Is.EqualTo(27));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CapturedCoreFactsUseCapturedNamespacesAndPreserveNullSourceNode(bool nullSource)
        {
            using ISessionBinding binding = SourceBinding().Object;
            var source = new WotEventSource(binding);
            Variant[] values = CoreValues();
            if (nullSource)
            {
                values[CoreIndex("SourceNode")] = new Variant(NodeId.Null);
            }
            WotCapturedEvent captured = WotCapturedEvent.Capture(
                source, WotCapturedEvent.RequiredSelectClauses, values);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured.Source, Is.SameAs(source));
                Assert.That(captured.HasEventId, Is.True);
                Assert.That(captured.EventId, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3 })));
                Assert.That(captured.HasEventType, Is.True);
                Assert.That(captured.EventType, Is.EqualTo(new ExpandedNodeId("EventType", SourceNamespace)));
                Assert.That(captured.HasSourceNode, Is.True);
                Assert.That(captured.SourceNode, Is.EqualTo(nullSource
                    ? ExpandedNodeId.Null : new ExpandedNodeId("Pump", SourceNamespace)));
                Assert.That(captured.HasConditionId, Is.True);
                Assert.That(captured.ConditionId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                Assert.That(captured.HasBranchId, Is.True);
                Assert.That(captured.BranchId.IsNull, Is.True);
                Assert.That(captured.HasTime, Is.True);
                Assert.That(captured.Time, Is.EqualTo(SourceTime));
                Assert.That(captured.HasReceiveTime, Is.True);
            }
        }

        [Test]
        public void BaseCaptureDoesNotRequestPrivateConditionOperands()
        {
            var filter = new EventFilter
            {
                SelectClauses = [Operand(Ua.ObjectTypeIds.BaseEventType, Attributes.Value, "Message")]
            };
            ArrayOf<int> indexes = OpcUaWotBindingChannel.AppendRequiredEventFields(
                filter, WotEventSelectClauses.Default);
            Assert.That(indexes.Count, Is.EqualTo(8));
            Assert.That(filter.SelectClauses.Count, Is.EqualTo(8));
            Assert.That(indexes[6], Is.Zero);
            foreach (SimpleAttributeOperand operand in filter.SelectClauses)
            {
                Assert.That(operand.TypeDefinitionId, Is.EqualTo(Ua.ObjectTypeIds.BaseEventType));
                Assert.That(operand.AttributeId, Is.EqualTo(Attributes.Value));
            }
        }

        [TestCase("EventId")]
        [TestCase("EventType")]
        [TestCase("SourceNode")]
        [TestCase("ConditionId")]
        [TestCase("BranchId")]
        [TestCase("Time")]
        [TestCase("ReceiveTime")]
        public void MissingPrivateCoreFactsRemainAbsent(string field)
        {
            using ISessionBinding binding = SourceBinding().Object;
            var source = new WotEventSource(binding);
            Variant[] values = CoreValues();
            values[CoreIndex(field == "ConditionId" ? string.Empty : field)] = Variant.Null;
            WotCapturedEvent captured = WotCapturedEvent.Capture(
                source, WotCapturedEvent.RequiredSelectClauses, values);
            bool present = field switch
            {
                "EventId" => captured.HasEventId,
                "EventType" => captured.HasEventType,
                "SourceNode" => captured.HasSourceNode,
                "ConditionId" => captured.HasConditionId,
                "BranchId" => captured.HasBranchId,
                "Time" => captured.HasTime,
                "ReceiveTime" => captured.HasReceiveTime,
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            Assert.That(present, Is.False);
        }

        [TestCase("EventId")]
        [TestCase("ConditionId")]
        [TestCase("Time")]
        public void MalformedPrivateCoreFactsFailWithTheirActualStatus(string field)
        {
            using ISessionBinding binding = SourceBinding().Object;
            var source = new WotEventSource(binding);
            Variant[] values = CoreValues();
            values[CoreIndex(field == "ConditionId" ? string.Empty : field)] = new Variant(42);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                WotCapturedEvent.Capture(source, WotCapturedEvent.RequiredSelectClauses, values));
            Assert.That(error!.StatusCode, Is.EqualTo(field == "ConditionId"
                ? StatusCodes.BadNodeIdInvalid : StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void PrivateConditionIdentityRejectsAnUncapturedNamespaceIndex()
        {
            using ISessionBinding binding = SourceBinding().Object;
            var source = new WotEventSource(binding);
            Variant[] values = CoreValues();
            values[CoreIndex(string.Empty)] = new Variant(new NodeId("ConditionA", 2));
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                WotCapturedEvent.Capture(source, WotCapturedEvent.RequiredSelectClauses, values));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void PrivateCaptureRejectsMismatchedFieldCounts(int difference)
        {
            using ISessionBinding binding = SourceBinding().Object;
            var source = new WotEventSource(binding);
            var values = new Variant[WotCapturedEvent.RequiredSelectClauses.Count + difference];
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                WotCapturedEvent.Capture(source, WotCapturedEvent.RequiredSelectClauses, values));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidatedSourceRejectsPrivateCaptureBeforeReadingFields(bool disposed)
        {
            Mock<ISessionBinding> provider = SourceBinding();
            using ISessionBinding binding = provider.Object;
            var source = new WotEventSource(binding);
            provider.SetupGet(value => value.Disposed).Returns(disposed);
            provider.SetupGet(value => value.IsCurrent).Returns(disposed);
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                WotCapturedEvent.Capture(source, WotCapturedEvent.RequiredSelectClauses, []));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase("ConditionId")]
        [TestCase("NullConditionId")]
        [TestCase("EventId")]
        [TestCase("BranchId")]
        public async Task MissingPrivateConditionIdentityCannotUseTheAuthoredActionOwnerAsync(string field)
        {
            using ISessionBinding sessionBinding = SourceBinding().Object;
            var source = new WotEventSource(sessionBinding);
            Variant[] values = CoreValues();
            string path = field is "ConditionId" or "NullConditionId" ? string.Empty : field;
            values[CoreIndex(path)] = field == "NullConditionId" ? new Variant(NodeId.Null) : Variant.Null;
            WotCapturedEvent captured = WotCapturedEvent.Capture(
                source, WotCapturedEvent.RequiredSelectClauses, values);
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable([Ua.Namespaces.OpcUa, "urn:private-capture:local"])
            };
            var form = new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", "urn:opcfoundation:wot:binding:opcua"),
                Bindings.WotAffordanceKind.Event, "condition", "/events/condition/forms/0",
                WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                new WotEndpointDescriptor("opc.tcp", "source", 4840, "opc.tcp://source:4840"),
                new WotAddressingDescriptor("i=2253"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                null, new WotEventSelection([new WotResolvedEventSelectClause("i=2782", "ConditionName")],
                    WotEventSelectionOrigin.Standard), null);
            var slot = new WotBindingChannelSlot(form, Mock.Of<IWotBindingChannelFactory>());
            await using var eventSource = new WotProjectedEventSource(form, slot);
            var notifier = new BaseObjectState(null) { NodeId = new NodeId("Notifier", 1) };
            int creations = 0;
            var projected = new WotProjectedEventBinding(
                context, eventSource, notifier, new NodeId("EventType", 1), null,
                new ExpandedNodeId("AuthoredOwner", SourceNamespace), 16, TimeProvider.System,
                "urn:private-capture:document", "/events/condition", new WotProjectedEventRouteRegistry(),
                isCondition: true, createCondition: (nodeId, _) =>
                {
                    creations++;
                    return new ValueTask<ConditionState>(new ConditionState(null) { NodeId = nodeId });
                });
            WotNotification notification = new WotNotification(new DataValue(Variant.Null)).WithCapturedEvent(captured);
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await projected.ProjectAsync(notification, CancellationToken.None).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(field is "ConditionId" or "NullConditionId"
                ? StatusCodes.BadNodeIdInvalid : StatusCodes.BadEventIdUnknown));
            Assert.That(creations, Is.Zero);
            Assert.That(projected.TryResolveOwnEventId(captured.EventId, out _), Is.False);
            projected.Clear();
            await slot.DisposeAsync().ConfigureAwait(false);
        }

        private static Mock<ISessionBinding> SourceBinding()
        {
            var provider = new Mock<ISessionBinding>();
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris = new NamespaceTable([Ua.Namespaces.OpcUa, SourceNamespace]);
            provider.SetupGet(value => value.Endpoint).Returns(new EndpointDescription
            {
                Server = new ApplicationDescription { ApplicationUri = "urn:private-capture:source-server" },
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });
            provider.SetupGet(value => value.SessionId).Returns(new NodeId("session", 0));
            provider.SetupGet(value => value.MessageContext).Returns(context);
            provider.SetupGet(value => value.IsCurrent).Returns(true);
            return provider;
        }

        private static int CoreIndex(string path)
        {
            return WotCapturedEvent.RequiredSelectClauses.ToList().FindIndex(clause => clause.BrowsePath == path);
        }

        private static Variant[] CoreValues()
        {
            return WotCapturedEvent.RequiredSelectClauses.ConvertAll(clause => clause.BrowsePath switch
            {
                "EventId" => new Variant(ByteString.From(new byte[] { 1, 2, 3 })),
                "EventType" => new Variant(new NodeId("EventType", 1)),
                "SourceNode" => new Variant(new NodeId("Pump", 1)),
                "" => new Variant(new NodeId("ConditionA", 1)),
                "BranchId" => new Variant(NodeId.Null),
                "ConditionClassId" => new Variant(Ua.ObjectTypeIds.ProcessConditionClassType),
                "SourceName" or "ConditionName" or "ClientUserId" => new Variant("source-value"),
                "Message" or "ConditionClassName" or "EnabledState" or "Comment" =>
                    new Variant(new LocalizedText("source-text")),
                "Severity" or "LastSeverity" => new Variant((ushort)612),
                "Time" or "ReceiveTime" or "Quality/SourceTimestamp" or
                    "LastSeverity/SourceTimestamp" or "Comment/SourceTimestamp" => new Variant(SourceTime),
                "Retain" or "EnabledState/Id" => new Variant(true),
                "Quality" => new Variant(StatusCodes.Good),
                _ => throw new ArgumentOutOfRangeException(nameof(clause))
            }).Span.ToArray();
        }

        private static SimpleAttributeOperand Operand(NodeId type, uint attribute, params string[] path)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = type,
                AttributeId = attribute,
                BrowsePath = path.ToArrayOf().ConvertAll(QualifiedName.From)
            };
        }

        private const string SourceNamespace = "urn:private-capture:source";
        private static readonly DateTimeUtc SourceTime = new(2026, 8, 1, 1, 2, 3);
    }
}
#endif
