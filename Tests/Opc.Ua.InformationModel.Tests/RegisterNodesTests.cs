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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Conformance.Tests.ViewServices
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "011")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "Err-005")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "006")]
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
        [Property("ConformanceUnit", "View RegisterNodes")]
        [Property("Tag", "001")]
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
