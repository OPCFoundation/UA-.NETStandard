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

using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Hybrid integration tests for the source-generated NodeManager
    /// extensibility surface. These tests use hand-written stand-ins that
    /// are structurally identical to what
    /// <c>NodeManagerGenerator</c> emits, plus a Boiler-style subclass +
    /// custom factory, to prove the extension story works end-to-end
    /// against the real <c>Opc.Ua.Server</c> types.
    /// </summary>
    /// <remarks>
    /// Spinning up a real <c>StandardServer</c> from a unit test is too
    /// heavy. Instead we exercise the two extension seams the generator
    /// design depends on:
    /// <list type="number">
    ///   <item>A subclass of the (un-sealed, virtual-member) factory
    ///   adds a second namespace and returns a manager subclass.</item>
    ///   <item>The runtime fluent <see cref="NodeManagerBuilder"/> wires
    ///   per-node callbacks against nodes resolved from a
    ///   subclass-supplied predefined-node table — mirroring what the
    ///   generated <c>CreateAddressSpace</c> does after
    ///   <c>base.CreateAddressSpace</c>.</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [Category("Fluent")]
    public class GeneratedManagerHybridTests
    {
        private const string kPrimaryUri = "http://example.org/UA/Primary/";
        private const string kInstanceUri = "http://example.org/UA/Primary/Instance";

        // ----- Stand-in for a generated factory: matches the template
        // output (public partial, virtual members, single namespace).
        private class FakeGeneratedFactory : INodeManagerFactory
        {
            public virtual ArrayOf<string> NamespacesUris
                => new ArrayOf<string>(new[] { kPrimaryUri });

            public virtual INodeManager Create(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return Mock.Of<INodeManager>();
            }
        }

        // ----- A Boiler-style customization: adds a second namespace and
        // returns a custom manager. The fact this compiles is itself part
        // of the contract — generated factory must NOT be sealed.
        private sealed class CustomFactory : FakeGeneratedFactory
        {
            public INodeManager LastCreated { get; private set; }

            public override ArrayOf<string> NamespacesUris
                => new ArrayOf<string>(new[] { kPrimaryUri, kInstanceUri });

            public override INodeManager Create(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                INodeManager mgr = Mock.Of<INodeManager>();
                LastCreated = mgr;
                return mgr;
            }
        }

        [Test]
        public void Subclass_CanAddSecondNamespace()
        {
            var factory = new CustomFactory();

            string[] uris = factory.NamespacesUris.ToArray();

            Assert.That(uris, Is.EqualTo(new[] { kPrimaryUri, kInstanceUri }));
        }

        [Test]
        public void Subclass_CanSwapManagerInstance()
        {
            var factory = new CustomFactory();

            INodeManager created = factory.Create(
                Mock.Of<IServerInternal>(),
                new ApplicationConfiguration());

            Assert.That(created, Is.Not.Null);
            Assert.That(created, Is.SameAs(factory.LastCreated));
        }

        [Test]
        public void Subclass_PreservesINodeManagerFactoryContract()
        {
            INodeManagerFactory asInterface = new CustomFactory();

            // Must round-trip through the framework interface.
            Assert.That(asInterface.NamespacesUris.Count, Is.EqualTo(2));
            Assert.That(
                asInterface.Create(Mock.Of<IServerInternal>(), new ApplicationConfiguration()),
                Is.Not.Null);
        }

        // ----- Stand-in for the generated CreateAddressSpace post-base
        // wiring: build a fluent builder against a subclass-supplied
        // predefined-node graph and replay NotifyNodeAdded for each
        // existing node. This mirrors the template at
        // NodeManagerTemplates.cs (CreateAddressSpace section).
        [Test]
        public void GeneratedManagerWiringSequence_FiresOnNodeAddedAfterSeal()
        {
            const ushort kNs = 2;
            var ctx = new SystemContext(telemetry: null);

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs)
            };
            var var1 = new BaseDataVariableState(root)
            {
                NodeId = new NodeId("Root.Var1", kNs),
                BrowseName = new QualifiedName("Var1", kNs),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            root.AddChild(var1);

            var roots = new System.Collections.Generic.Dictionary<QualifiedName, NodeState>
            {
                [root.BrowseName] = root
            };
            var byId = new System.Collections.Generic.Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [var1.NodeId] = var1
            };

            var builder = new NodeManagerBuilder(
                ctx,
                Mock.Of<IAsyncNodeManager>(),
                kNs,
                q => roots.TryGetValue(q, out NodeState n) ? n : null,
                id => byId.TryGetValue(id, out NodeState n) ? n : null,
                _ => System.Array.Empty<NodeState>());

            int nodeAddedCount = 0;
            builder.Node("Root/Var1").OnNodeAdded((_, _) => nodeAddedCount++);

            // The contract: Configure registers callbacks, Seal closes the
            // builder, then NotifyNodeAdded replays for predefined nodes.
            builder.Seal();
            foreach (NodeState n in byId.Values)
            {
                builder.Dispatcher.NotifyNodeAdded(ctx, n);
            }

            Assert.That(nodeAddedCount, Is.EqualTo(1),
                "OnNodeAdded must fire exactly once for the wired predefined node");
        }
    }
}
