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

using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class AnalogAttributeWriteRegressionTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task AnalogMetadataWriteDoesNotApplyInstrumentRange(bool synchronous, bool writable)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var syncManager = new SynchronousManager(server.Object))
            using (var asyncManager = new AsynchronousManager(server.Object))
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.Write, RequestLifetime.None))
            {
                ushort ns = synchronous ? syncManager.NamespaceIndex : asyncManager.NamespaceIndexes[0];
                var node = new AnalogItemState(null)
                {
                    NodeId = new NodeId("Analog", ns),
                    BrowseName = new QualifiedName("Analog", ns),
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.Scalar,
                    Value = 20.0,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    WriteMask = writable ? AttributeWriteMask.AccessLevel : AttributeWriteMask.None,
                    UserWriteMask = writable ? AttributeWriteMask.AccessLevel : AttributeWriteMask.None
                };
                node.InstrumentRange = new PropertyState<Range>.Implementation<StructureBuilder<Range>>(node)
                {
                    NodeId = new NodeId("Range", ns),
                    BrowseName = new QualifiedName(BrowseNames.InstrumentRange),
                    Value = new Range { Low = 10, High = 100 }
                };
                if (synchronous)
                {
                    syncManager.Register(node);
                }
                else
                {
                    await asyncManager.RegisterAsync(node).ConfigureAwait(false);
                }
                ArrayOf<WriteValue> writes =
                [
                    new WriteValue
                    {
                        NodeId = node.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(40.0)
                    },
                    new WriteValue
                    {
                        NodeId = node.NodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(5.0)
                    },
                    new WriteValue
                    {
                        NodeId = node.NodeId,
                        AttributeId = Attributes.AccessLevel,
                        Value = new DataValue(AccessLevels.CurrentRead)
                    }
                ];
                var errors = new ServiceResult[3];

                if (synchronous)
                {
                    syncManager.Write(context, writes, errors);
                }
                else
                {
                    await asyncManager.WriteAsync(context, writes, errors).ConfigureAwait(false);
                }

                Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
                Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(errors[2]?.StatusCode ?? StatusCodes.Good,
                    Is.EqualTo(writable ? StatusCodes.Good : StatusCodes.BadNotWritable));
                Assert.That(node.Value, Is.EqualTo(new Variant(40.0)));
                Assert.That(node.AccessLevel,
                    Is.EqualTo(writable ? AccessLevels.CurrentRead : AccessLevels.CurrentReadOrWrite));
            }
        }

        private sealed class SynchronousManager : CustomNodeManager2
        {
            public SynchronousManager(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:analog-sync")
            {
            }

            public void Register(NodeState node)
            {
                AddPredefinedNode(SystemContext, node);
            }
        }

        private sealed class AsynchronousManager : AsyncCustomNodeManager
        {
            public AsynchronousManager(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:analog-async")
            {
            }

            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }
        }
    }
}
