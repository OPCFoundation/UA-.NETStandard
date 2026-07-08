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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ServerTargetVariableWriter"/>: it
    /// builds a single WriteValue, returns the server status, and never throws
    /// on a service fault.
    /// </summary>
    [TestFixture]
    public sealed class ServerTargetVariableWriterTests
    {
        [Test]
        public void ConstructorNullSessionThrows()
        {
            Assert.That(
                () => new ServerTargetVariableWriter(null!, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public async Task WriteAsyncBuildsWriteValueAndReturnsSessionStatusAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            ArrayOf<WriteValue> captured = default;
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<WriteValue>, CancellationToken>((w, _) => captured = w)
                .Returns(new ValueTask<ArrayOf<StatusCode>>(
                    new[] { StatusCodes.Good }.ToArrayOf()));
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());

            var node = new NodeId(7u);
            var value = new DataValue(new Variant(42.0));
            StatusCode status = await writer
                .WriteAsync(node, Attributes.Value, "1:2", value)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(status), Is.True);
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].NodeId, Is.EqualTo(node));
            Assert.That(captured[0].AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(captured[0].IndexRange, Is.EqualTo("1:2"));
            Assert.That(captured[0].Value.WrappedValue, Is.EqualTo(new Variant(42.0)));
        }

        [Test]
        public async Task WriteAsyncEmptyResultsReturnsBadAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<StatusCode>>([]));
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());

            StatusCode status = await writer
                .WriteAsync(new NodeId(1u), Attributes.Value, null, new DataValue(new Variant(1)))
                .ConfigureAwait(false);

            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        [Test]
        public async Task WriteAsyncServiceFaultReturnsFaultStatusAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(StatusCodes.BadNodeIdUnknown, "x"));
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());

            StatusCode status = await writer
                .WriteAsync(new NodeId(1u), Attributes.Value, null, new DataValue(new Variant(1)))
                .ConfigureAwait(false);

            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task WriteAsyncUnexpectedFaultReturnsBadCommunicationErrorAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("transport"));
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());

            StatusCode status = await writer
                .WriteAsync(new NodeId(1u), Attributes.Value, null, new DataValue(new Variant(1)))
                .ConfigureAwait(false);

            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        [Test]
        public async Task WriteAsyncConnectsWhenDisconnectedAsync()
        {
            var session = new Mock<IServerSession>();
            session.SetupGet(s => s.IsConnected).Returns(false);
            session.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<StatusCode>>(
                    new[] { StatusCodes.Good }.ToArrayOf()));
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());

            await writer
                .WriteAsync(new NodeId(1u), Attributes.Value, null, new DataValue(new Variant(1)))
                .ConfigureAwait(false);

            session.Verify(s => s.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void WriteAsyncCancellationPropagates()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await writer
                    .WriteAsync(
                        new NodeId(1u), Attributes.Value, null,
                        new DataValue(new Variant(1)), cts.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
