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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests that verify adapter read, write and action-call paths resolve
    /// browse-path sentinels before issuing OPC UA service calls.
    /// </summary>
    [TestFixture]
    public sealed class BrowsePathResolutionTests
    {
        [Test]
        public async Task CyclicReadStrategyReadsResolvedBrowsePathNodeId()
        {
            NodeId browsePath = NodeBrowsePath.ToNodeId("/2:Demo/2:X");
            NodeId resolvedNodeId = new(42u);
            ArrayOf<ReadValueId> captured = ArrayOf<ReadValueId>.Null;
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.ResolveNodeIdAsync(browsePath, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(resolvedNodeId));
            session.Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ReadValueId>, CancellationToken>((nodes, _) => captured = nodes)
                .Returns(new ValueTask<ArrayOf<DataValue>>([
                    new DataValue(new Variant(123))
                ]));
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            await strategy.ReadAsync([
                new ReadValueId { NodeId = browsePath, AttributeId = Attributes.Value }
            ]).ConfigureAwait(false);

            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].NodeId, Is.EqualTo(resolvedNodeId));
        }

        [Test]
        public async Task CyclicReadStrategyReadsNumericNodeIdUnchanged()
        {
            NodeId numericNodeId = new(11u);
            ArrayOf<ReadValueId> captured = ArrayOf<ReadValueId>.Null;
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ReadValueId>, CancellationToken>((nodes, _) => captured = nodes)
                .Returns(new ValueTask<ArrayOf<DataValue>>([
                    new DataValue(new Variant(123))
                ]));
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            await strategy.ReadAsync([
                new ReadValueId { NodeId = numericNodeId, AttributeId = Attributes.Value }
            ]).ConfigureAwait(false);

            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].NodeId, Is.EqualTo(numericNodeId));
        }

        [Test]
        public async Task ServerTargetVariableWriterWritesResolvedBrowsePathNodeId()
        {
            NodeId browsePath = NodeBrowsePath.ToNodeId("/2:Demo/2:X");
            NodeId resolvedNodeId = new(42u);
            ArrayOf<WriteValue> captured = ArrayOf<WriteValue>.Null;
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.ResolveNodeIdAsync(browsePath, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(resolvedNodeId));
            session.Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<WriteValue>, CancellationToken>((nodes, _) => captured = nodes)
                .Returns(new ValueTask<ArrayOf<StatusCode>>([
                    (StatusCode)StatusCodes.Good
                ]));
            var writer = new ServerTargetVariableWriter(session.Object, AdapterTestHelpers.Telemetry());

            StatusCode status = await writer.WriteAsync(
                browsePath,
                Attributes.Value,
                null,
                new DataValue(new Variant(123))).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(status), Is.True);
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].NodeId, Is.EqualTo(resolvedNodeId));
        }

        [Test]
        public async Task ServerActionHandlerCallsResolvedBrowsePathObjectAndMethodIds()
        {
            NodeId objectBrowsePath = NodeBrowsePath.ToNodeId("/2:Demo");
            NodeId methodBrowsePath = NodeBrowsePath.ToNodeId("/2:Demo/2:Reset");
            NodeId resolvedObjectId = new(1001u);
            NodeId resolvedMethodId = new(1002u);
            NodeId capturedObjectId = NodeId.Null;
            NodeId capturedMethodId = NodeId.Null;
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session.Setup(s => s.ResolveNodeIdAsync(objectBrowsePath, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(resolvedObjectId));
            session.Setup(s => s.ResolveNodeIdAsync(methodBrowsePath, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(resolvedMethodId));
            session.Setup(s => s.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<Variant>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<NodeId, NodeId, ArrayOf<Variant>, CancellationToken>((objectId, methodId, _, _) =>
                {
                    capturedObjectId = objectId;
                    capturedMethodId = methodId;
                })
                .Returns(new ValueTask<RemoteCallResult>(
                    new RemoteCallResult((StatusCode)StatusCodes.Good, [])));
            var map = new ActionMethodMap().Add(
                "Reset",
                objectBrowsePath,
                methodBrowsePath);
            var handler = new ServerActionHandler(session.Object, map, AdapterTestHelpers.Telemetry());

            PubSubActionHandlerResult result = await handler.HandleAsync(new PubSubActionInvocation
            {
                Target = new PubSubActionTarget { ActionName = "Reset" },
                InputFields = [new DataSetField { Name = "Input", Value = new Variant(1) }]
            }).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(capturedObjectId, Is.EqualTo(resolvedObjectId));
            Assert.That(capturedMethodId, Is.EqualTo(resolvedMethodId));
        }
    }
}
