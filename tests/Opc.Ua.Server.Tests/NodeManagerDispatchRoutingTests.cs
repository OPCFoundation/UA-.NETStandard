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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that Read and Write are dispatched only to the node managers that serve
    /// the namespaces of the requested nodes, with unchanged results.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [Parallelizable]
    public sealed class NodeManagerDispatchRoutingTests
    {
        private const string kNamespaceA = "http://test.org/UA/RoutingA/";
        private const string kNamespaceB = "http://test.org/UA/RoutingB/";

        [Test]
        public async Task ReadCallsOnlyTheNodeManagersOfTheRequestedNamespaceAsync()
        {
            await RunAsync(async (sut, a, b, nsA, nsB) =>
            {
                (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                    Context(), 0, TimestampsToReturn.Neither,
                    [Value(new NodeId(1, nsA)), Value(new NodeId(2, nsA))]).ConfigureAwait(false);

                Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant("A")));
                Assert.That(values[1].WrappedValue, Is.EqualTo(new Variant("A")));
                Assert.That(a.ReadCalls, Is.EqualTo(1));
                Assert.That(b.ReadCalls, Is.Zero, "a node manager without a requested namespace is skipped");
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadCallsEveryOwningNodeManagerOnceAndKeepsResultOrderAsync()
        {
            await RunAsync(async (sut, a, b, nsA, nsB) =>
            {
                (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                    Context(), 0, TimestampsToReturn.Neither,
                    [
                        Value(new NodeId(1, nsB)),
                        Value(new NodeId(1, nsA)),
                        Value(new NodeId(2, nsB))
                    ]).ConfigureAwait(false);

                Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant("B")));
                Assert.That(values[1].WrappedValue, Is.EqualTo(new Variant("A")));
                Assert.That(values[2].WrappedValue, Is.EqualTo(new Variant("B")));
                Assert.That(a.ReadCalls, Is.EqualTo(1));
                Assert.That(b.ReadCalls, Is.EqualTo(1));
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadOfAnUnroutedNamespaceStillCallsEveryNodeManagerAsync()
        {
            await RunAsync(async (sut, a, b, nsA, nsB) =>
            {
                (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                    Context(), 0, TimestampsToReturn.Neither,
                    [Value(new NodeId(1, nsA)), Value(new NodeId(1, 250))]).ConfigureAwait(false);

                Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant("A")));
                Assert.That(values[1].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(a.ReadCalls, Is.EqualTo(1));
                Assert.That(b.ReadCalls, Is.EqualTo(1), "an unrouted node keeps the call to every node manager");
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadOfOnlyInvalidItemsCallsNoNodeManagerAsync()
        {
            await RunAsync(async (sut, a, b, nsA, nsB) =>
            {
                (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                    Context(), 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(1, nsA), AttributeId = 999 }]).ConfigureAwait(false);

                Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
                Assert.That(a.ReadCalls, Is.Zero);
                Assert.That(b.ReadCalls, Is.Zero);
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task SharedNamespaceKeepsDispatchOrderBetweenItsNodeManagersAsync()
        {
            // the first node manager of a namespace in dispatch order processes the node; the
            // second one is still called and sees the node as processed, as before.
            var second = new FakeNodeManager("A2", kNamespaceA);
            await RunAsync(async (sut, a, b, nsA, nsB) =>
            {
                Assert.That(sut.NamespaceManagers[nsA], Has.Count.EqualTo(2));

                (ArrayOf<DataValue> values, _) = await sut.ReadAsync(
                    Context(), 0, TimestampsToReturn.Neither,
                    [Value(new NodeId(1, nsA))]).ConfigureAwait(false);

                Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant("A")));
                Assert.That(a.ReadCalls, Is.EqualTo(1));
                Assert.That(second.ReadCalls, Is.EqualTo(1));
                Assert.That(b.ReadCalls, Is.Zero);
            }, second).ConfigureAwait(false);
        }

        [Test]
        public async Task WriteCallsOnlyTheNodeManagersOfTheRequestedNamespaceAsync()
        {
            await RunAsync(async (sut, a, b, nsA, nsB) =>
            {
                (ArrayOf<StatusCode> results, _) = await sut.WriteAsync(
                    Context(),
                    [
                        new WriteValue
                        {
                            NodeId = new NodeId(1, nsB),
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(1))
                        }
                    ]).ConfigureAwait(false);

                Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(b.WriteCalls, Is.EqualTo(1));
                Assert.That(a.WriteCalls, Is.Zero);
            }).ConfigureAwait(false);
        }

        private delegate Task Scenario(
            MasterNodeManager sut,
            FakeNodeManager a,
            FakeNodeManager b,
            ushort nsA,
            ushort nsB);

        private static async Task RunAsync(Scenario scenario, FakeNodeManager? sameNamespaceAsA = null)
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var a = new FakeNodeManager("A", kNamespaceA);
                var b = new FakeNodeManager("B", kNamespaceB);
                IAsyncNodeManager[] managers = sameNamespaceAsA == null
                    ? [a.Object, b.Object]
                    : [a.Object, sameNamespaceAsA.Object, b.Object];
                using var sut = new MasterNodeManager(
                    server.CurrentInstance, fixture.Config, null, managers);
                NamespaceTable namespaces = server.CurrentInstance.NamespaceUris;
                a.NamespaceIndex = (ushort)namespaces.GetIndex(kNamespaceA);
                b.NamespaceIndex = (ushort)namespaces.GetIndex(kNamespaceB);
                if (sameNamespaceAsA != null)
                {
                    sameNamespaceAsA.NamespaceIndex = a.NamespaceIndex;
                }
                await scenario(sut, a, b, a.NamespaceIndex, b.NamespaceIndex).ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static OperationContext Context()
        {
            return new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
        }

        private static ReadValueId Value(NodeId nodeId)
        {
            return new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
        }

        /// <summary>
        /// A node manager that owns every node of its namespace and counts its calls.
        /// </summary>
        private sealed class FakeNodeManager
        {
            public FakeNodeManager(string tag, string namespaceUri)
            {
                var mock = new Mock<IAsyncNodeManager>();
                mock.SetupGet(m => m.NamespaceUris).Returns([namespaceUri]);
                mock.Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, double maxAge, ArrayOf<ReadValueId> nodesToRead,
                        IList<DataValue> values, IList<ServiceResult> errors, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_readCalls);
                        for (int ii = 0; ii < nodesToRead.Count; ii++)
                        {
                            if (!nodesToRead[ii].Processed && nodesToRead[ii].NodeId.NamespaceIndex == NamespaceIndex)
                            {
                                nodesToRead[ii].Processed = true;
                                values[ii] = new DataValue(new Variant(tag));
                                errors[ii] = ServiceResult.Good;
                            }
                        }
                        return default;
                    });
                mock.Setup(m => m.WriteAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<ArrayOf<WriteValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext context, ArrayOf<WriteValue> nodesToWrite,
                        IList<ServiceResult> errors, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_writeCalls);
                        for (int ii = 0; ii < nodesToWrite.Count; ii++)
                        {
                            if (!nodesToWrite[ii].Processed && nodesToWrite[ii].NodeId.NamespaceIndex == NamespaceIndex)
                            {
                                nodesToWrite[ii].Processed = true;
                                errors[ii] = ServiceResult.Good;
                            }
                        }
                        return default;
                    });
                Object = mock.Object;
            }

            public IAsyncNodeManager Object { get; }

            public int ReadCalls => Volatile.Read(ref m_readCalls);

            public int WriteCalls => Volatile.Read(ref m_writeCalls);

            public ushort NamespaceIndex { get; set; }

            private int m_readCalls;
            private int m_writeCalls;
        }
    }
}
