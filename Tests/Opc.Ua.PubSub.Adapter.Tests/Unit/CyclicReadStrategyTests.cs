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
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
{
    /// <summary>
    /// Unit tests for <see cref="CyclicReadStrategy"/>: delegation to the
    /// session Read, fail-soft fault mapping and cancellation propagation.
    /// </summary>
    [TestFixture]
    public sealed class CyclicReadStrategyTests
    {
        [Test]
        public void ConstructorNullSessionThrows()
        {
            Assert.That(
                () => new CyclicReadStrategy(null!, AdapterTestHelpers.Telemetry()),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("session"));
        }

        [Test]
        public void ConstructorNullTelemetryThrows()
        {
            var session = new Mock<IServerSession>().Object;
            Assert.That(
                () => new CyclicReadStrategy(session, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("telemetry"));
        }

        [Test]
        public async Task ReadAsyncEmptyInputReturnsEmptyWithoutSessionCallAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            ArrayOf<DataValue> result = await strategy
                .ReadAsync(ArrayOf<ReadValueId>.Empty)
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.Zero);
            session.Verify(
                s => s.ReadAsync(It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ReadAsyncDelegatesToSessionReadAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            var values = new[]
            {
                new DataValue(new Variant(11.0)),
                new DataValue(new Variant(22.0))
            }.ToArrayOf();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<DataValue>>(values));
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            var nodes = new[]
            {
                new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value },
                new ReadValueId { NodeId = new NodeId(2u), AttributeId = Attributes.Value }
            }.ToArrayOf();

            ArrayOf<DataValue> result = await strategy.ReadAsync(nodes).ConfigureAwait(false);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].WrappedValue, Is.EqualTo(new Variant(11.0)));
            Assert.That(result[1].WrappedValue, Is.EqualTo(new Variant(22.0)));
        }

        [Test]
        public async Task ReadAsyncConnectsWhenSessionDisconnectedAsync()
        {
            var session = new Mock<IServerSession>();
            session.SetupGet(s => s.IsConnected).Returns(false);
            session.Setup(s => s.ConnectAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<DataValue>>(
                    new[] { new DataValue(new Variant(1)) }.ToArrayOf()));
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            await strategy
                .ReadAsync(new[] { new ReadValueId { NodeId = new NodeId(1u) } }.ToArrayOf())
                .ConfigureAwait(false);

            session.Verify(s => s.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReadAsyncServiceFaultReturnsPositionallyAlignedBadValuesAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Throws(ServiceResultException.Create(
                    StatusCodes.BadSessionClosed, "boom"));
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            var nodes = new[]
            {
                new ReadValueId { NodeId = new NodeId(1u) },
                new ReadValueId { NodeId = new NodeId(2u) },
                new ReadValueId { NodeId = new NodeId(3u) }
            }.ToArrayOf();

            ArrayOf<DataValue> result = await strategy.ReadAsync(nodes).ConfigureAwait(false);

            Assert.That(result.Count, Is.EqualTo(3));
            for (int i = 0; i < result.Count; i++)
            {
                Assert.That(StatusCode.IsBad(result[i].StatusCode), Is.True);
                Assert.That(result[i].StatusCode.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            }
        }

        [Test]
        public async Task ReadAsyncUnexpectedFaultReturnsBadCommunicationErrorAsync()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("transport"));
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            ArrayOf<DataValue> result = await strategy
                .ReadAsync(new[] { new ReadValueId { NodeId = new NodeId(1u) } }.ToArrayOf())
                .ConfigureAwait(false);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadCommunicationError));
        }

        [Test]
        public void ReadAsyncCancellationPropagates()
        {
            Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());
            var strategy = new CyclicReadStrategy(session.Object, AdapterTestHelpers.Telemetry());

            Assert.That(
                async () => await strategy
                    .ReadAsync(new[] { new ReadValueId { NodeId = new NodeId(1u) } }.ToArrayOf())
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
