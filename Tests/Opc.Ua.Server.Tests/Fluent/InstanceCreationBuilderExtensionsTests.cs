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
    /// Tests for <see cref="InstanceCreationBuilderExtensions"/> —
    /// fluent <c>CreateInstance&lt;TState&gt;</c> helper.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class InstanceCreationBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(global::Opc.Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        private static (NodeManagerBuilder Builder, BaseObjectState Root) CreateBuilder()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState> { [root.NodeId] = root };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, root);
        }

        [Test]
        public void CreateInstanceAttachesNewChild()
        {
            (NodeManagerBuilder b, BaseObjectState root) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IInstanceBuilder<BaseObjectState> ib = nb.CreateInstance(
                new QualifiedName("Pump#2", kNs),
                p => new BaseObjectState(p));

            Assert.That(ib.Node, Is.Not.Null);
            Assert.That(ib.Node.BrowseName, Is.EqualTo(new QualifiedName("Pump#2", kNs)));
            Assert.That(ib.Node.SymbolicName, Is.EqualTo("Pump#2"));
            Assert.That(ib.Node.Parent, Is.SameAs(root));
            Assert.That(ib.Node.NodeId.IdentifierAsString, Is.EqualTo("Root_Pump#2"));
        }

        [Test]
        public void CreateInstanceWithTypeDefinitionIdStampsIt()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            var typeDef = new NodeId(1024u, kNs);

            IInstanceBuilder<BaseObjectState> ib = nb.CreateInstance(
                new QualifiedName("Pump#2", kNs),
                typeDef,
                p => new BaseObjectState(p));

            Assert.That(ib.Node.TypeDefinitionId, Is.EqualTo(typeDef));
        }

        [Test]
        public void AsNodeReturnsTypedNodeBuilder()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            IInstanceBuilder<BaseObjectState> ib = nb.CreateInstance(
                new QualifiedName("Pump#2", kNs),
                p => new BaseObjectState(p));
            INodeBuilder<BaseObjectState> typed = ib.AsNode();

            Assert.That(typed.Node, Is.SameAs(ib.Node));
            Assert.That(typed.Builder, Is.SameAs(b));
        }

        [Test]
        public void ConfigureInvokesActionWithTypedBuilder()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            bool invoked = false;
            nb.CreateInstance(
                new QualifiedName("Pump#2", kNs),
                p => new BaseObjectState(p))
                .Configure(typed =>
                {
                    invoked = true;
                    Assert.That(typed.Node, Is.Not.Null);
                });

            Assert.That(invoked, Is.True);
        }

        [Test]
        public void DoneReturnsToParentBuilder()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder back = nb.CreateInstance(
                new QualifiedName("Pump#2", kNs),
                p => new BaseObjectState(p))
                .Done();

            Assert.That(back, Is.SameAs(nb));
        }

        [Test]
        public void NullFactoryReturnThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => nb.CreateInstance<BaseObjectState>(
                    new QualifiedName("Pump#2", kNs),
                    p => null!))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void NullArgsThrowArgumentNullException()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<ArgumentNullException>(
                () => ((INodeBuilder)null!).CreateInstance(
                    new QualifiedName("X", kNs),
                    p => new BaseObjectState(p)));
            Assert.Throws<ArgumentNullException>(
                () => nb.CreateInstance<BaseObjectState>(QualifiedName.Null, p => new BaseObjectState(p)));
            Assert.Throws<ArgumentNullException>(
                () => nb.CreateInstance<BaseObjectState>(
                    new QualifiedName("X", kNs),
                    (Func<NodeState, BaseObjectState>)null!));
        }

        [Test]
        public void MultipleInstancesUnderSameParent()
        {
            (NodeManagerBuilder b, BaseObjectState root) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.CreateInstance(new QualifiedName("Pump#1", kNs), p => new BaseObjectState(p));
            nb.CreateInstance(new QualifiedName("Pump#2", kNs), p => new BaseObjectState(p));

            var children = new List<BaseInstanceState>();
            root.GetChildren(null!, children);
            Assert.That(children, Has.Count.EqualTo(2));
        }

        [Test]
        public void ChainedConfigurationViaAsNode()
        {
            (NodeManagerBuilder b, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> typed = nb.CreateInstance(
                new QualifiedName("Pump#2", kNs),
                p => new BaseObjectState(p))
                .AsNode();

            // typed builder supports AddObject (G6 reuse)
            INodeBuilder<BaseObjectState> sub = typed.AddObject(
                new QualifiedName("Sub", kNs));
            Assert.That(sub.Node.Parent, Is.SameAs(typed.Node));
        }
    }
}
