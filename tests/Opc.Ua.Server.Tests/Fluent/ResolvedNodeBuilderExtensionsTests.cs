// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

// CA2000: the NodeState instances created here are owned by the builder under
// test for the lifetime of the test method.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Covers the fluent view over a node the caller already holds — the
    /// entry point managers use for nodes materialized after the
    /// <c>Configure</c> pass, which the resolving <c>Node(...)</c>
    /// overloads cannot find.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class ResolvedNodeBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        /// <summary>
        /// A builder whose resolvers find nothing at all, so a passing
        /// test can only be explained by the node being taken as given.
        /// </summary>
        private static NodeManagerBuilder CreateEmptyBuilder()
        {
            return new NodeManagerBuilder(
                new SystemContext(telemetry: null!),
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: _ => null!,
                nodeIdResolver: _ => null!,
                typeIdResolver: _ => []);
        }

        private static MethodState CreateMethod()
        {
            return new MethodState(parent: null)
            {
                NodeId = new NodeId("Runtime.M1", kNs),
                BrowseName = new QualifiedName("M1", kNs),
                DisplayName = new LocalizedText("M1")
            };
        }

        [Test]
        public void WrapsNodeThatTheResolversCannotFind()
        {
            NodeManagerBuilder b = CreateEmptyBuilder();
            MethodState method = CreateMethod();

            // The resolving overload has nothing to go on ...
            Assert.Throws<ServiceResultException>(() => b.Node(method.NodeId));

            // ... while the node itself is all this overload needs.
            INodeBuilder<MethodState> nb = b.Node(method);

            Assert.That(nb.Node, Is.SameAs(method));
            Assert.That(nb.Builder, Is.SameAs(b));
        }

        [Test]
        public void WrappedBuilderWiresHandlersOnTheNode()
        {
            NodeManagerBuilder b = CreateEmptyBuilder();
            MethodState method = CreateMethod();

            b.Node(method).OnCall(
                (context, m, objectId, inputArguments, outputArguments) => ServiceResult.Good);

            Assert.That(method.OnCallMethod2, Is.Not.Null);
        }

        [Test]
        public async Task WorksAfterTheBuilderIsSealedAsync()
        {
            NodeManagerBuilder b = CreateEmptyBuilder();
            MethodState method = CreateMethod();
            await b.SealAsync().ConfigureAwait(false);

            // Sealing fails further lookups against the predefined-node
            // graph, but a node handed in directly was never part of it.
            INodeBuilder<MethodState> nb = b.Node(method);

            Assert.That(nb.Node, Is.SameAs(method));
        }

        [Test]
        public void NullArgumentsThrow()
        {
            NodeManagerBuilder b = CreateEmptyBuilder();

            Assert.Throws<ArgumentNullException>(() => b.Node((MethodState)null!));
            Assert.Throws<ArgumentNullException>(
                () => ((INodeManagerBuilder)null!).Node(CreateMethod()));
        }
    }
}
