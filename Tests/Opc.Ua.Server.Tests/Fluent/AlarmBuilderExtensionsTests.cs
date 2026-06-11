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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
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
                nodeManager: Mock.Of<IAsyncNodeManager>(),
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
            Assert.That(ab.Alarm.NodeId.IdentifierAsString, Is.EqualTo("Root_OverTemp"));
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
        public void MonitorVariableSetsSourceNodeAndName()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState src) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("OverTemp", kNs))
                .MonitorVariable(src);

            Assert.That(ab.Alarm.SourceNode!.Value, Is.EqualTo(src.NodeId));
            Assert.That(ab.Alarm.SourceName!.Value, Is.EqualTo("Temp"));
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
    }
}
