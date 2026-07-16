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
    /// Tests for <see cref="SupervisionBuilderExtensions"/> — fluent
    /// NAMUR / bool-transition helpers.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class SupervisionBuilderExtensionsTests
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
            BaseDataVariableState<bool> BoolVar)
            CreateBuilder()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var boolVar =
                BaseDataVariableState<bool>.With<VariantBuilder>(root);
            boolVar.NodeId = new NodeId("Root.Flag", kNs);
            boolVar.BrowseName = new QualifiedName("Flag", kNs);
            boolVar.DisplayName = new LocalizedText("Flag");
            boolVar.DataType = DataTypeIds.Boolean;
            boolVar.ValueRank = ValueRanks.Scalar;
            boolVar.Value = false;
            root.AddChild(boolVar);

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [boolVar.NodeId] = boolVar
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, root, boolVar);
        }

        [Test]
        public void OnRisingEdgeFiresOnFalseToTrue()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState<bool> v) = CreateBuilder();
            IVariableBuilder<bool> vb = b.Variable<bool>(new NodeId("Root.Flag", kNs));

            int rising = 0;
            vb.OnRisingEdge(_ => rising++);

            // false -> true: should fire
            v.Value = true;
            v.ClearChangeMasks(null!, includeChildren: false);
            Assert.That(rising, Is.EqualTo(1));
        }

        [Test]
        public void OnFallingEdgeFiresOnTrueToFalse()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState<bool> v) = CreateBuilder();
            v.Value = true; // start true
            v.ClearChangeMasks(null!, includeChildren: false);

            IVariableBuilder<bool> vb = b.Variable<bool>(new NodeId("Root.Flag", kNs));

            int falling = 0;
            vb.OnFallingEdge(_ => falling++);

            v.Value = false;
            v.ClearChangeMasks(null!, includeChildren: false);
            Assert.That(falling, Is.EqualTo(1));
        }

        [Test]
        public void NoEdgeFiresOnSameValue()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState<bool> v) = CreateBuilder();
            IVariableBuilder<bool> vb = b.Variable<bool>(new NodeId("Root.Flag", kNs));

            int rising = 0;
            int falling = 0;
            vb.OnRisingEdge(_ => rising++).OnFallingEdge(_ => falling++);

            v.Value = false;
            v.ClearChangeMasks(null!, includeChildren: false);
            // First write to value (false -> false) -> no rising, no falling

            Assert.That(rising, Is.Zero);
            Assert.That(falling, Is.Zero);
        }

        [Test]
        public void ActivatesAlarmFlipsAlarmActiveState()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState<bool> v) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            IAlarmBuilder<NonExclusiveLimitAlarmState> ab = nb.CreateLimitAlarm(
                new QualifiedName("CavitationAlarm", kNs));

            IVariableBuilder<bool> vb = b.Variable<bool>(new NodeId("Root.Flag", kNs));
            vb.ActivatesAlarm(ab);

            // Transition false -> true: should activate alarm
            v.Value = true;
            v.ClearChangeMasks(null!, includeChildren: false);

            Assert.That(ab.Alarm.ActiveState!.Id!.Value, Is.True);

            // Transition true -> false: should deactivate
            v.Value = false;
            v.ClearChangeMasks(null!, includeChildren: false);

            Assert.That(ab.Alarm.ActiveState.Id.Value, Is.False);
        }

        [Test]
        public void NullArgsThrowArgumentNullException()
        {
            (NodeManagerBuilder b, _, _) = CreateBuilder();
            IVariableBuilder<bool> vb = b.Variable<bool>(new NodeId("Root.Flag", kNs));
            IVariableBuilder<bool> nullBuilder = null!;

            Assert.Throws<ArgumentNullException>(() => nullBuilder.OnRisingEdge(_ => { }));
            Assert.Throws<ArgumentNullException>(() => vb.OnRisingEdge(null!));
            Assert.Throws<ArgumentNullException>(() => nullBuilder.OnFallingEdge(_ => { }));
            Assert.Throws<ArgumentNullException>(() => vb.OnFallingEdge(null!));
        }

        [Test]
        public void MultipleHandlersAllFire()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState<bool> v) = CreateBuilder();
            IVariableBuilder<bool> vb = b.Variable<bool>(new NodeId("Root.Flag", kNs));

            int handlerA = 0;
            int handlerB = 0;
            vb.OnRisingEdge(_ => handlerA++)
              .OnRisingEdge(_ => handlerB++);

            v.Value = true;
            v.ClearChangeMasks(null!, includeChildren: false);

            Assert.That(handlerA, Is.EqualTo(1));
            Assert.That(handlerB, Is.EqualTo(1));
        }
    }
}
