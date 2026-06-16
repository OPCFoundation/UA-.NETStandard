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
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Unit tests for the internal <see cref="FunctionalGroupBuilder"/>
    /// class — verifies constructor validation, Organizes-reference
    /// wiring, and the Configure escape hatch.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class FunctionalGroupBuilderTests
    {
        private static FunctionalGroupState NewGroup()
        {
            return new FunctionalGroupState(parent: null)
            {
                NodeId = new NodeId("fg-1", 2),
                BrowseName = new QualifiedName("Group", 2)
            };
        }

        private static SystemContext NewContext()
        {
            return new SystemContext(telemetry: null!);
        }

        private static INodeBuilder NewNodeBuilderMock(NodeState? node = null)
        {
            var mock = new Mock<INodeBuilder>();
            mock.SetupGet(b => b.Node).Returns(node ?? new BaseObjectState(null));
            return mock.Object;
        }

        [Test]
        public void ConstructorThrowsOnNullGroup()
        {
            INodeBuilder node = NewNodeBuilderMock();
            SystemContext ctx = NewContext();

            Assert.Throws<ArgumentNullException>(
                () => new FunctionalGroupBuilder(group: null!, node, ctx));
        }

        [Test]
        public void ConstructorThrowsOnNullNode()
        {
            FunctionalGroupState group = NewGroup();
            SystemContext ctx = NewContext();

            Assert.Throws<ArgumentNullException>(
                () => new FunctionalGroupBuilder(group, node: null!, ctx));
        }

        [Test]
        public void ConstructorThrowsOnNullContext()
        {
            FunctionalGroupState group = NewGroup();
            INodeBuilder node = NewNodeBuilderMock();

            Assert.Throws<ArgumentNullException>(
                () => new FunctionalGroupBuilder(group, node, context: null!));
        }

        [Test]
        public void OrganizesNodeStateAddsForwardAndInverseReferences()
        {
            FunctionalGroupState group = NewGroup();
            INodeBuilder node = NewNodeBuilderMock();
            var target = new BaseObjectState(null)
            {
                NodeId = new NodeId("target-1", 2)
            };

            var builder = new FunctionalGroupBuilder(group, node, NewContext());
            IFunctionalGroupBuilder result = builder.Organizes(target);

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                group.ReferenceExists(Types.ReferenceTypeIds.Organizes, false, target.NodeId),
                Is.True,
                "Group should have a forward Organizes reference to target.");
            Assert.That(
                target.ReferenceExists(Types.ReferenceTypeIds.Organizes, true, group.NodeId),
                Is.True,
                "Target should have an inverse Organizes reference back to group.");
        }

        [Test]
        public void OrganizesNodeIdAddsForwardReferenceOnly()
        {
            FunctionalGroupState group = NewGroup();
            INodeBuilder node = NewNodeBuilderMock();
            var targetId = new NodeId("target-2", 2);

            var builder = new FunctionalGroupBuilder(group, node, NewContext());
            IFunctionalGroupBuilder result = builder.Organizes(targetId);

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                group.ReferenceExists(Types.ReferenceTypeIds.Organizes, false, targetId),
                Is.True);
        }

        [Test]
        public void OrganizesThrowsOnNullNodeId()
        {
            FunctionalGroupState group = NewGroup();
            INodeBuilder node = NewNodeBuilderMock();

            var builder = new FunctionalGroupBuilder(group, node, NewContext());

            Assert.Throws<ArgumentNullException>(() => builder.Organizes(NodeId.Null));
        }

        [Test]
        public void ConfigureThrowsOnNullAction()
        {
            FunctionalGroupState group = NewGroup();
            INodeBuilder node = NewNodeBuilderMock();

            var builder = new FunctionalGroupBuilder(group, node, NewContext());

            Assert.Throws<ArgumentNullException>(() => builder.Configure(null!));
        }

        [Test]
        public void ConfigureInvokesActionWithNodeView()
        {
            FunctionalGroupState group = NewGroup();
            INodeBuilder node = NewNodeBuilderMock();

            var builder = new FunctionalGroupBuilder(group, node, NewContext());
            INodeBuilder? observed = null;
            IFunctionalGroupBuilder result = builder.Configure(n => observed = n);

            Assert.That(result, Is.SameAs(builder));
            Assert.That(observed, Is.SameAs(node));
            Assert.That(builder.Node, Is.SameAs(node));
            Assert.That(builder.Group, Is.SameAs(group));
        }
    }
}
