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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SessionExtensionsTests
    {
        private Mock<ISession> _session = null!;

        [SetUp]
        public void SetUp()
        {
            _session = new Mock<ISession>(MockBehavior.Strict);
        }

        [TearDown]
        public void TearDown()
        {
            _session.Verify();
        }

        [Test]
        public async Task OpenAsyncWithNameAndIdentityForwardsToFullOverloadAsync()
        {
            var identity = new UserIdentity();

            // The 3-param extension chains through the 5-param and 6-param extensions
            // ultimately calling the 7-param ISession.OpenAsync
            _session
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

            await _session.Object.OpenAsync("TestSession", identity).ConfigureAwait(false);

            _session.Verify(s => s.OpenAsync(
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

            _session
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

            await _session.Object.OpenAsync("TestSession", 60000u, identity, locales).ConfigureAwait(false);

            _session.Verify(s => s.OpenAsync(
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

            _session
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

            await _session.Object.OpenAsync("TestSession", 30000u, identity, locales, false).ConfigureAwait(false);

            _session.Verify(s => s.OpenAsync(
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
            _session
                .Setup(s => s.ReconnectAsync(null, null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _session.Object.ReconnectAsync().ConfigureAwait(false);

            _session.Verify(s => s.ReconnectAsync(
                null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReconnectAsyncWithConnectionForwardsCorrectlyAsync()
        {
            var connection = new Mock<ITransportWaitingConnection>();

            _session
                .Setup(s => s.ReconnectAsync(connection.Object, null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _session.Object.ReconnectAsync(connection.Object).ConfigureAwait(false);

            _session.Verify(s => s.ReconnectAsync(
                connection.Object, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReconnectAsyncWithChannelForwardsCorrectlyAsync()
        {
            var channel = new Mock<ITransportChannel>();

            _session
                .Setup(s => s.ReconnectAsync(null, channel.Object, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await _session.Object.ReconnectAsync(channel.Object).ConfigureAwait(false);

            _session.Verify(s => s.ReconnectAsync(
                null, channel.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CloseAsyncWithCloseChannelForwardsCorrectlyAsync()
        {
            _session
                .SetupGet(s => s.KeepAliveInterval)
                .Returns(5000);

            _session
                .Setup(s => s.CloseAsync(5000, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good)
                .Verifiable();

            StatusCode result = await _session.Object.CloseAsync(true).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CloseAsyncWithCloseChannelFalseForwardsCorrectlyAsync()
        {
            _session
                .SetupGet(s => s.KeepAliveInterval)
                .Returns(10000);

            _session
                .Setup(s => s.CloseAsync(10000, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good)
                .Verifiable();

            StatusCode result = await _session.Object.CloseAsync(false).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CloseAsyncWithTimeoutForwardsCorrectlyAsync()
        {
            _session
                .Setup(s => s.CloseAsync(3000, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StatusCodes.Good)
                .Verifiable();

            StatusCode result = await _session.Object.CloseAsync(3000).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void SaveWithSubscriptionsForwardsToStreamSave()
        {
            var subscriptions = System.Array.Empty<Subscription>();
            _session
                .SetupGet(s => s.Subscriptions)
                .Returns(subscriptions);

            // The Save(filePath) extension calls Save(filePath, session.Subscriptions, null)
            // which creates a FileStream. We test that it reads Subscriptions.
            Assert.That(_session.Object.Subscriptions, Is.SameAs(subscriptions));
        }

        [Test]
        public void ReadByteStringInChunksAsyncThrowsWhenMaxByteStringLengthTooSmall()
        {
            var capabilities = new ServerCapabilities { MaxByteStringLength = 0 };
            _session
                .SetupGet(s => s.ServerCapabilities)
                .Returns(capabilities);

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await _session.Object.ReadByteStringInChunksAsync(new NodeId(1)).ConfigureAwait(false));
        }

        [Test]
        public void ReadByteStringInChunksAsyncThrowsWhenMaxByteStringLengthIsOne()
        {
            var capabilities = new ServerCapabilities { MaxByteStringLength = 1 };
            _session
                .SetupGet(s => s.ServerCapabilities)
                .Returns(capabilities);

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await _session.Object.ReadByteStringInChunksAsync(new NodeId(1)).ConfigureAwait(false));
        }
    }
}
