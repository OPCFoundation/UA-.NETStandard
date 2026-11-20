/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for View Service Set – RegisterNodes / UnregisterNodes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("RegisterNodes")]
    public class RegisterNodesTests : TestFixture
    {
        [Test]
        public async Task RegisterSingleNodeReturnsGoodAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null,
                new NodeId[] { nodeId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                response.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task RegisterMultipleNodesReturnsGoodAsync()
        {
            ArrayOf<NodeId> nodeIds = new NodeId[]
            {
                ToNodeId(Constants.ScalarStaticInt32),
                ToNodeId(Constants.ScalarStaticDouble),
                ToNodeId(Constants.ScalarStaticString),
                ToNodeId(Constants.ScalarStaticBoolean)
            }.ToArrayOf();

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null,
                nodeIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(nodeIds.Count));

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                response.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task UnregisterNodesReturnsGoodAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            RegisterNodesResponse regResp = await Session.RegisterNodesAsync(
                null,
                new NodeId[] { nodeId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            UnregisterNodesResponse unregResp = await Session.UnregisterNodesAsync(
                null,
                regResp.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(unregResp.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        public async Task ReadUsingRegisteredNodeIdsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            RegisterNodesResponse regResp = await Session.RegisterNodesAsync(
                null,
                new NodeId[] { nodeId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            NodeId registeredId = regResp.RegisteredNodeIds[0];

            // Use registered NodeId for reading
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = registeredId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResp.Results[0].StatusCode), Is.True);

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                regResp.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task RegisterNodesWithInvalidNodeIdStillSucceedsAsync()
        {
            // Per spec, RegisterNodes should not validate node existence
            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null,
                new NodeId[] { Constants.InvalidNodeId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(1));

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                response.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task RegisterAndReadMultipleRegisteredNodesAsync()
        {
            ArrayOf<NodeId> nodeIds = new NodeId[]
            {
                ToNodeId(Constants.ScalarStaticInt32),
                ToNodeId(Constants.ScalarStaticDouble)
            }.ToArrayOf();

            RegisterNodesResponse regResp = await Session.RegisterNodesAsync(
                null,
                nodeIds,
                CancellationToken.None).ConfigureAwait(false);

            var readValueIdList = new List<ReadValueId>();
            foreach (NodeId id in regResp.RegisteredNodeIds)
            {
                readValueIdList.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Value });
            }
            ArrayOf<ReadValueId> readValueIds = readValueIdList.ToArray().ToArrayOf();

            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResp.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(readResp.Results[0].StatusCode), Is.True);
            Assert.That(StatusCode.IsGood(readResp.Results[1].StatusCode), Is.True);

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                regResp.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task RegisterSameNodeTwiceReturnsResultsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            RegisterNodesResponse response = await Session.RegisterNodesAsync(
                null,
                new NodeId[] { nodeId, nodeId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RegisteredNodeIds.Count, Is.EqualTo(2));

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                response.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task WriteUsingRegisteredNodeIdAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            RegisterNodesResponse regResp = await Session.RegisterNodesAsync(
                null,
                new NodeId[] { nodeId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            NodeId registeredId = regResp.RegisteredNodeIds[0];

            // Write via registered ID
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = registeredId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(99))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(writeResp.Results[0]), Is.True);

            // Cleanup
            await Session.UnregisterNodesAsync(
                null,
                regResp.RegisteredNodeIds,
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
