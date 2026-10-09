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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="AlarmBuilderExtensions"/> — fluent
    /// <c>CreateLimitAlarm</c> / <c>CreateExclusiveLimitAlarm</c> /
    /// <c>CreateOffNormalAlarm</c> helpers.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class AlarmBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        private static (NodeManagerBuilder Builder, BaseObjectState Root,
            BaseDataVariableState Source)
            CreateBuilder()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var src = new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId("Temp", kNs),
                BrowseName = new QualifiedName("Temp", kNs),
                DisplayName = new LocalizedText("Temp"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar
            };

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [src.NodeId] = src
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: FluentTestNodeManager.Create(kNs),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, root, src);
        }

        [Test]
        public void CreateLimitAlarmAttachesToParent()
        {
            (NodeManagerBuilder b, BaseObjectState root, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs));

            Assert.That(ab.Alarm, Is.Not.Null);
            Assert.That(ab.Alarm.BrowseName, Is.EqualTo(new QualifiedName("OverTemp", kNs)));
            Assert.That(ab.Alarm.Parent, Is.SameAs(root));
            // the identifier is the factory's canonical browse path, so the
            // assertion names the path rather than restating its encoding.
            Assert.That(
                ab.Alarm.NodeId.IdentifierAsString,
                Does.Contain("Root").And.Contain("OverTemp"));
        }

        [Test]
        public void CreateLimitAlarmAddsEventSourceReferencesAndPromotesNotifier()
        {
            (NodeManagerBuilder b, BaseObjectState root, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> first = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs));
            _ = nb.CreateLimitAlarm(new QualifiedName("OverPressure", kNs));

            var references = new List<IReference>();
            root.GetReferences(b.Context, references);
            Assert.That(
                root.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.Not.Zero);
            Assert.That(
                references,
                Has.Exactly(2).Matches<IReference>(reference =>
                    reference.ReferenceTypeId == ReferenceTypeIds.HasEventSource &&
                    !reference.IsInverse));

            references.Clear();
            first.Alarm.GetReferences(b.Context, references);
            Assert.That(
                references,
                Has.Exactly(1).Matches<IReference>(reference =>
                    reference.ReferenceTypeId == ReferenceTypeIds.HasEventSource &&
                    reference.IsInverse &&
                    reference.TargetId == root.NodeId));
        }

        [Test]
        public void WithLimitsSetsAllFour()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs))
                .WithLimits(highHigh: 380.0, high: 370.0, low: 273.0, lowLow: 263.0);

            Assert.That(ab.Alarm.HighHighLimit, Is.Not.Null);
            Assert.That(ab.Alarm.HighHighLimit!.Value, Is.EqualTo(380.0));
            Assert.That(ab.Alarm.HighLimit!.Value, Is.EqualTo(370.0));
            Assert.That(ab.Alarm.LowLimit!.Value, Is.EqualTo(273.0));
            Assert.That(ab.Alarm.LowLowLimit!.Value, Is.EqualTo(263.0));
        }

        [Test]
        public void WithLimitsSkipsNaNSlots()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs))
                .WithLimits(high: 100.0);

            Assert.That(ab.Alarm.HighLimit, Is.Not.Null);
            Assert.That(ab.Alarm.HighLimit!.Value, Is.EqualTo(100.0));
            Assert.That(ab.Alarm.HighHighLimit, Is.Null,
                "Only High limit was set; HighHigh must remain null.");
            Assert.That(ab.Alarm.LowLimit, Is.Null);
            Assert.That(ab.Alarm.LowLowLimit, Is.Null);
        }

        [Test]
        public void MonitorVariableMakesTheVariableTheConditionSource()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState src) = CreateBuilder();
            var rootId = new NodeId("Root", kNs);
            INodeBuilder nb = b.Node(rootId);

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs));

            // Part 9 5.8.2: InputNode names a Variable, never the parent Object.
            Assert.That(ab.Alarm.InputNode!.Value.IsNull, Is.True);

            ab.MonitorVariable(src);

            // The Variable becomes the ConditionSource the events name (Part 9 5.5.2):
            // HasCondition to the alarm and HasEventSource from the owning Object.
            Assert.That(ab.Alarm.SourceNode!.Value, Is.EqualTo(src.NodeId));
            Assert.That(ab.Alarm.SourceName!.Value, Is.EqualTo("Temp"));
            Assert.That(ab.Alarm.InputNode.Value, Is.EqualTo(src.NodeId));
            Assert.That(src.ReferenceExists(ReferenceTypeIds.HasCondition, false, ab.Alarm.NodeId), Is.True);
            Assert.That(ab.Alarm.Parent!.NodeId, Is.EqualTo(rootId));
            Assert.That(
                ab.Alarm.Parent.ReferenceExists(ReferenceTypeIds.HasEventSource, false, src.NodeId),
                Is.True);
        }

        [Test]
        public void OnAcknowledgeWiresHandler()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs))
                .OnAcknowledge((ctx, c, eventId, comment) => ServiceResult.Good);

            Assert.That(ab.Alarm.OnAcknowledge, Is.Not.Null);
        }

        [Test]
        public void OnConfirmWiresHandler()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs))
                .OnConfirm((ctx, c, eventId, comment) => ServiceResult.Good);

            Assert.That(ab.Alarm.OnConfirm, Is.Not.Null);
        }

        [Test]
        public void ConfigureAlarmEscapeHatchInvokesAction()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            bool invoked = false;
            nb.CreateLimitAlarm(new QualifiedName("OverTemp", kNs))
              .ConfigureAlarm(alarm =>
              {
                  invoked = true;
                  Assert.That(alarm, Is.Not.Null);
              });

            Assert.That(invoked, Is.True);
        }

        [Test]
        public void DoneReturnsToParentBuilder()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder back = nb.CreateLimitAlarm(new QualifiedName("OverTemp", kNs))
                .WithLimits(high: 100)
                .Done();

            Assert.That(back, Is.SameAs(nb));
        }

        [Test]
        public void CreateExclusiveLimitAlarmReturnsExclusiveBuilder()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<ExclusiveLimitAlarmState> ab = nb.CreateExclusiveLimitAlarm(
                new QualifiedName("ExclHigh", kNs));

            Assert.That(ab.Alarm, Is.InstanceOf<ExclusiveLimitAlarmState>());
        }

        [Test]
        public void CreateOffNormalAlarmReturnsOffNormalBuilder()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<OffNormalAlarmState> ab = nb.CreateOffNormalAlarm(
                new QualifiedName("OffNormal", kNs));

            Assert.That(ab.Alarm, Is.InstanceOf<OffNormalAlarmState>());
        }

        [Test]
        public void CreateLimitAlarmNullArgsThrow()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            INodeBuilder nullBuilder = null!;

            Assert.Throws<ArgumentNullException>(
                () => nullBuilder.CreateLimitAlarm(new QualifiedName("X", kNs)));
            Assert.Throws<ArgumentNullException>(
                () => nb.CreateLimitAlarm(QualifiedName.Null));
        }

        [Test]
        public void CreateLimitAlarmOnVariableThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState src) = CreateBuilder();
            IVariableBuilder<double> variable = b.Variable<double>(src.NodeId);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => variable.CreateLimitAlarm(new QualifiedName("OverTemp", kNs)));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void CreateAlarmAttachesTheFactoryStateLikeTheDedicatedHelpers()
        {
            (NodeManagerBuilder b, BaseObjectState root, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            NodeState? handedParent = null;

            IAlarmBuilder<TripAlarmState> ab = nb.CreateAlarm(
                new QualifiedName("Trip", kNs),
                parent =>
                {
                    handedParent = parent;
                    return new TripAlarmState(parent);
                });

            var references = new List<IReference>();
            root.GetReferences(b.Context, references);
            Assert.Multiple(() =>
            {
                Assert.That(handedParent, Is.SameAs(root));
                Assert.That(ab.Alarm, Is.InstanceOf<TripAlarmState>());
                Assert.That(ab.Builder, Is.SameAs(nb));
                Assert.That(ab.Alarm.Parent, Is.SameAs(root));
                Assert.That(ab.Alarm.BrowseName, Is.EqualTo(new QualifiedName("Trip", kNs)));
                Assert.That(ab.Alarm.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasCondition));
                Assert.That(
                    ab.Alarm.NodeId.IdentifierAsString,
                    Does.Contain("Root").And.Contain("Trip"));
                Assert.That(ab.Alarm.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.TripAlarmType));
                Assert.That(ab.Alarm.EnabledState!.Id!.Value, Is.True);
                Assert.That(ab.Alarm.SourceNode!.Value, Is.EqualTo(root.NodeId));
                Assert.That(ab.Alarm.SourceName!.Value, Is.EqualTo("Root"));
                Assert.That(ab.Alarm.ConditionName!.Value, Is.EqualTo("Trip"));
                Assert.That(root.EventNotifier & EventNotifiers.SubscribeToEvents, Is.Not.Zero);
                Assert.That(
                    references,
                    Has.Exactly(1).Matches<IReference>(reference =>
                        reference.ReferenceTypeId == ReferenceTypeIds.HasEventSource &&
                        !reference.IsInverse &&
                        reference.TargetId == ab.Alarm.NodeId));
            });
        }

        [Test]
        public void CreateAlarmAcceptsAConditionThatIsNoAlarm()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<AcknowledgeableConditionState> ab = nb.CreateAlarm(
                new QualifiedName("Inspection", kNs),
                parent => new AcknowledgeableConditionState(parent))
                .OnAcknowledge((ctx, condition, eventId, comment) => ServiceResult.Good);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => ab.WithLimits(high: 1.0));
            Assert.Multiple(() =>
            {
                Assert.That(ab.Alarm.OnAcknowledge, Is.Not.Null);
                Assert.That(ab.Alarm.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.AcknowledgeableConditionType));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            });
        }

        [Test]
        public void CreateAlarmNullArgsThrow()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            INodeBuilder nullBuilder = null!;

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() => nullBuilder.CreateAlarm(
                    new QualifiedName("X", kNs),
                    parent => new OffNormalAlarmState(parent)));
                Assert.Throws<ArgumentNullException>(() => nb.CreateAlarm(
                    QualifiedName.Null,
                    parent => new OffNormalAlarmState(parent)));
                Assert.Throws<ArgumentNullException>(() => nb.CreateAlarm<OffNormalAlarmState>(
                    new QualifiedName("X", kNs),
                    null!));
            });
        }

        [Test]
        public void CreateAlarmRejectsAFactoryWithoutAnAlarm()
        {
            (NodeManagerBuilder b, BaseObjectState root, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<InvalidOperationException>(() => nb.CreateAlarm<OffNormalAlarmState>(
                new QualifiedName("X", kNs),
                _ => null!));

            var references = new List<IReference>();
            root.GetReferences(b.Context, references);
            Assert.That(
                references,
                Has.None.Matches<IReference>(reference =>
                    reference.ReferenceTypeId == ReferenceTypeIds.HasEventSource),
                "a refused alarm leaves the parent untouched");
        }

        [Test]
        public void CreateAlarmOnVariableThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState src) = CreateBuilder();
            IVariableBuilder<double> variable = b.Variable<double>(src.NodeId);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => variable.CreateAlarm(
                    new QualifiedName("Trip", kNs),
                    parent => new TripAlarmState(parent)));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }
    }
}
