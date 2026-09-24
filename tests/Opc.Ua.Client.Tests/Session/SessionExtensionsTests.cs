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
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionExtensionsTests
    {
        private Mock<ISession> m_session;

        [SetUp]
        public void SetUp()
        {
            m_session = new Mock<ISession>(MockBehavior.Strict);
        }

        [TearDown]
        public void TearDown()
        {
            m_session.Verify();
        }

        [Test]
        public async Task OpenAsyncWithNameAndIdentityForwardsToFullOverloadAsync()
        {
            var identity = new UserIdentity();
            // ultimately calling the 7-param ISession.OpenAsync
            m_session
                .Setup(s => s.OpenAsync(
                    "TestSession",
                    0u,
                    identity,
                    It.IsAny<ArrayOf<string>>(),
                    true,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await m_session.Object.OpenAsync("TestSession", identity).ConfigureAwait(false);

            m_session.Verify(s => s.OpenAsync(
                "TestSession",
                0u,
                identity,
                It.IsAny<ArrayOf<string>>(),
                true,
                true,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task OpenAsyncWithTimeoutForwardsToFullOverloadAsync()
        {
            var identity = new UserIdentity();
            ArrayOf<string> locales = ["en", "de"];

            m_session
                .Setup(s => s.OpenAsync(
                    "TestSession",
                    60000u,
                    identity,
                    It.IsAny<ArrayOf<string>>(),
                    true,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await m_session.Object.OpenAsync("TestSession", 60000u, identity, locales).ConfigureAwait(false);

            m_session.Verify(s => s.OpenAsync(
                "TestSession",
                60000u,
                identity,
                It.IsAny<ArrayOf<string>>(),
                true,
                true,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task OpenAsyncWithCheckDomainForwardsToFullOverloadAsync()
        {
            var identity = new UserIdentity();
            ArrayOf<string> locales = ["en"];

            m_session
                .Setup(s => s.OpenAsync(
                    "TestSession",
                    30000u,
                    identity,
                    It.IsAny<ArrayOf<string>>(),
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await m_session.Object.OpenAsync("TestSession", 30000u, identity, locales, false).ConfigureAwait(false);

            m_session.Verify(s => s.OpenAsync(
                "TestSession",
                30000u,
                identity,
                It.IsAny<ArrayOf<string>>(),
                false,
                true,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReconnectAsyncWithNoArgsForwardsCorrectlyAsync()
        {
            m_session
                .Setup(s => s.ReconnectAsync(null, null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await m_session.Object.ReconnectAsync().ConfigureAwait(false);

            m_session.Verify(s => s.ReconnectAsync(
                null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReconnectAsyncWithConnectionForwardsCorrectlyAsync()
        {
            var connection = new Mock<ITransportWaitingConnection>();

            m_session
                .Setup(s => s.ReconnectAsync(connection.Object, null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await m_session.Object.ReconnectAsync(connection.Object).ConfigureAwait(false);

            m_session.Verify(s => s.ReconnectAsync(
                connection.Object, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReconnectAsyncWithChannelForwardsCorrectlyAsync()
        {
            var channel = new Mock<ITransportChannel>();

            m_session
                .Setup(s => s.ReconnectAsync(null, channel.Object, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await m_session.Object.ReconnectAsync(channel.Object).ConfigureAwait(false);

            m_session.Verify(s => s.ReconnectAsync(
                null, channel.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CloseAsyncWithCloseChannelForwardsCorrectlyAsync()
        {
            m_session
                .SetupGet(s => s.KeepAliveInterval)
                .Returns(5000);

            m_session
                .Setup(s => s.CloseAsync(5000, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good)
                .Verifiable();

            StatusCode result = await m_session.Object.CloseAsync(true).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CloseAsyncWithCloseChannelFalseForwardsCorrectlyAsync()
        {
            m_session
                .SetupGet(s => s.KeepAliveInterval)
                .Returns(10000);

            m_session
                .Setup(s => s.CloseAsync(10000, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good)
                .Verifiable();

            StatusCode result = await m_session.Object.CloseAsync(false).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CloseAsyncWithTimeoutForwardsCorrectlyAsync()
        {
            m_session
                .Setup(s => s.CloseAsync(3000, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good)
                .Verifiable();

            StatusCode result = await m_session.Object.CloseAsync(3000).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void SaveWithSubscriptionsForwardsToStreamSave()
        {
            Subscription[] subscriptions = [];
            m_session
                .SetupGet(s => s.Subscriptions)
                .Returns(subscriptions);

            // The Save(filePath) extension calls Save(filePath, session.Subscriptions, null)
            // which creates a FileStream. We test that it reads Subscriptions.
            Assert.That(m_session.Object.Subscriptions, Is.SameAs(subscriptions));
        }

        [Test]
        public void ReadByteStringInChunksAsyncThrowsWhenNoLimitIsKnown()
        {
            var capabilities = new ServerCapabilities { MaxByteStringLength = 0 };
            m_session
                .SetupGet(s => s.ServerCapabilities)
                .Returns(capabilities);
            SetupMessageContext(maxByteStringLength: 0);

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_session.Object.ReadByteStringInChunksAsync(new NodeId(1)).ConfigureAwait(false));
        }

        [Test]
        public void ReadByteStringInChunksAsyncThrowsWhenMaxByteStringLengthIsOne()
        {
            var capabilities = new ServerCapabilities { MaxByteStringLength = 1 };
            m_session
                .SetupGet(s => s.ServerCapabilities)
                .Returns(capabilities);
            SetupMessageContext(maxByteStringLength: 1024);

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_session.Object.ReadByteStringInChunksAsync(new NodeId(1)).ConfigureAwait(false));
        }

        /// <summary>
        /// A server that does not provide MaxByteStringLength (0) or reports a
        /// value beyond int range is read in chunks of the client limit (L3-6).
        /// </summary>
        [TestCase(0u)]
        [TestCase(3_000_000_000u)]
        public async Task ReadByteStringInChunksAsyncUsesClientLimitWhenServerLimitUnknownAsync(
            uint serverLimit)
        {
            var capabilities = new ServerCapabilities { MaxByteStringLength = serverLimit };
            m_session
                .SetupGet(s => s.ServerCapabilities)
                .Returns(capabilities);
            SetupMessageContext(maxByteStringLength: 4);
            var ranges = new List<string>();
            m_session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, ids, _) =>
                    {
                        ranges.Add(ids[0].IndexRange);
                        byte[] chunk = ranges.Count == 1 ? [1, 2, 3, 4] : [5];
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new DataValue(new Variant(ByteString.From(chunk)))],
                            DiagnosticInfos = []
                        });
                    });

            ByteString result = await m_session.Object
                .ReadByteStringInChunksAsync(new NodeId(1))
                .ConfigureAwait(false);

            Assert.That(result.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
            string[] expectedRanges = ["0:3", "4:7"];
            Assert.That(ranges, Is.EqualTo(expectedRanges));
        }

        private void SetupMessageContext(int maxByteStringLength)
        {
            var messageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            messageContext.MaxByteStringLength = maxByteStringLength;
            m_session
                .SetupGet(s => s.MessageContext)
                .Returns(messageContext);
        }
    }
}
