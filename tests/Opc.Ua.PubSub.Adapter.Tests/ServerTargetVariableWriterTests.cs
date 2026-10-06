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
        public async Task WriteAsyncDropsTimestampsWhenTargetRejectsThemAsync()
        {
            // Part 14 6.2.11.1: a received timestamp is not used when the target
            // Variable does not allow writing timestamps.
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            var written = new System.Collections.Generic.List<DataValue>();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns<ArrayOf<WriteValue>, CancellationToken>((w, _) =>
                {
                    DataValue value = w[0].Value;
                    written.Add(value);
                    StatusCode status = value.SourceTimestamp == DateTimeUtc.MinValue
                        ? StatusCodes.Good
                        : StatusCodes.BadWriteNotSupported;
                    return new ValueTask<ArrayOf<StatusCode>>(new[] { status }.ToArrayOf());
                });
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());
            var node = new NodeId(7u);
            var timestamp = new DateTimeUtc(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));

            StatusCode first = await writer
                .WriteAsync(node, Attributes.Value, null,
                    new DataValue(new Variant(1.0), StatusCodes.Good, timestamp))
                .ConfigureAwait(false);
            StatusCode second = await writer
                .WriteAsync(node, Attributes.Value, null,
                    new DataValue(new Variant(2.0), StatusCodes.Good, timestamp))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(first), Is.True);
            Assert.That(StatusCode.IsGood(second), Is.True);
            // first write is retried without timestamps; the second goes
            // straight to the timestamp-free write.
            Assert.That(written, Has.Count.EqualTo(3));
            Assert.That(written[0].SourceTimestamp, Is.EqualTo(timestamp));
            Assert.That(written[1].SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(written[1].WrappedValue, Is.EqualTo(new Variant(1.0)));
            Assert.That(written[2].SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(written[2].WrappedValue, Is.EqualTo(new Variant(2.0)));
        }

        [Test]
        public async Task WriteAsyncRejectedServerTimestampKeepsSourceTimestampAsync()
        {
            // the target accepts SourceTimestamps but no ServerTimestamp: dropping the
            // ServerTimestamp must not suppress the SourceTimestamp of later writes.
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            var written = new System.Collections.Generic.List<DataValue>();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns<ArrayOf<WriteValue>, CancellationToken>((w, _) =>
                {
                    DataValue value = w[0].Value;
                    written.Add(value);
                    StatusCode status = value.ServerTimestamp == DateTimeUtc.MinValue
                        ? StatusCodes.Good
                        : StatusCodes.BadWriteNotSupported;
                    return new ValueTask<ArrayOf<StatusCode>>(new[] { status }.ToArrayOf());
                });
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());
            var node = new NodeId(7u);
            var source = new DateTimeUtc(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
            var server = new DateTimeUtc(new DateTime(2026, 10, 5, 0, 0, 1, DateTimeKind.Utc));

            StatusCode first = await writer
                .WriteAsync(node, Attributes.Value, null,
                    new DataValue(new Variant(1.0), StatusCodes.Good, source, server))
                .ConfigureAwait(false);
            StatusCode second = await writer
                .WriteAsync(node, Attributes.Value, null,
                    new DataValue(new Variant(2.0), StatusCodes.Good, source, server))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(first), Is.True);
            Assert.That(StatusCode.IsGood(second), Is.True);
            Assert.That(written, Has.Count.EqualTo(3));
            Assert.That(written[1].ServerTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(written[1].SourceTimestamp, Is.EqualTo(source));
            Assert.That(written[2].ServerTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(written[2].SourceTimestamp, Is.EqualTo(source),
                "Only the rejected ServerTimestamp is remembered.");
        }

        [Test]
        public async Task WriteAsyncKeepsStatusCodeWhenDroppingTimestampsAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            var written = new System.Collections.Generic.List<DataValue>();
            session
                .Setup(s => s.WriteAsync(
                    It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Returns<ArrayOf<WriteValue>, CancellationToken>((w, _) =>
                {
                    written.Add(w[0].Value);
                    return new ValueTask<ArrayOf<StatusCode>>(
                        new[] { (StatusCode)StatusCodes.BadWriteNotSupported }.ToArrayOf());
                });
            var writer = new ServerTargetVariableWriter(
                session.Object, AdapterTestHelpers.Telemetry());
            var timestamp = new DateTimeUtc(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));

            StatusCode status = await writer
                .WriteAsync(new NodeId(7u), Attributes.Value, null,
                    new DataValue(new Variant(1.0), StatusCodes.UncertainLastUsableValue, timestamp))
                .ConfigureAwait(false);

            Assert.That(status.Code, Is.EqualTo(StatusCodes.BadWriteNotSupported));
            Assert.That(written, Has.Count.EqualTo(2));
            Assert.That(written[1].StatusCode.Code, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(written[1].SourceTimestamp, Is.EqualTo(DateTimeUtc.MinValue));
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
