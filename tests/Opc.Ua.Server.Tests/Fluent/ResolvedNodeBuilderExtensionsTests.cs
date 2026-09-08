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
                new SystemContext(telemetry: null),
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
