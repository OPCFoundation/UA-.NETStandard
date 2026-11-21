// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Covers the deprecated synchronous node manager API on purpose.

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

        /// <summary>
        /// A null value has no elements to check against the InstrumentRange, so it must
        /// not be treated as a scalar 0.0 and rejected with BadOutOfRange.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AnalogArrayNullValueWriteSkipsInstrumentRange(bool synchronous)
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
                    NodeId = new NodeId("AnalogArray", ns),
                    BrowseName = new QualifiedName("AnalogArray", ns),
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.OneDimension,
                    Value = ArrayOf.Wrapped(20.0, 30.0),
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite
                };
                node.InstrumentRange = new PropertyState<Range>.Implementation<StructureBuilder<Range>>(node)
                {
                    NodeId = new NodeId("ArrayRange", ns),
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
                        Value = new DataValue(Variant.Null)
                    }
                ];
                var errors = new ServiceResult[1];

                if (synchronous)
                {
                    syncManager.Write(context, writes, errors);
                }
                else
                {
                    await asyncManager.WriteAsync(context, writes, errors).ConfigureAwait(false);
                }

                Assert.That(errors[0]?.StatusCode ?? StatusCodes.Good, Is.EqualTo(StatusCodes.Good));
                Assert.That(node.Value.IsNull, Is.True);
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
