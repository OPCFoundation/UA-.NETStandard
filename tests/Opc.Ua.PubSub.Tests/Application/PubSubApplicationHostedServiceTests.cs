// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Unit tests for the
    /// <see cref="PubSubApplicationHostedService"/> IHostedService
    /// wrapper.
    /// </summary>
    [TestFixture]
    public class PubSubApplicationHostedServiceTests
    {
        [Test]
        public async Task StartAsync_ForwardsToApplication()
        {
            var app = new Mock<IPubSubApplication>(MockBehavior.Strict);
            app.SetupGet(a => a.ApplicationId).Returns("test-app");
            app.Setup(a => a.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var hosted = new PubSubApplicationHostedService(
                app.Object,
                NullLogger<PubSubApplicationHostedService>.Instance);
            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
            app.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task StopAsync_ForwardsToApplication()
        {
            var app = new Mock<IPubSubApplication>(MockBehavior.Strict);
            app.SetupGet(a => a.ApplicationId).Returns("test-app");
            app.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var hosted = new PubSubApplicationHostedService(
                app.Object,
                NullLogger<PubSubApplicationHostedService>.Instance);
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
            app.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void Constructor_NullApplication_Throws()
        {
            Assert.That(
                () => new PubSubApplicationHostedService(
                    null!,
                    NullLogger<PubSubApplicationHostedService>.Instance),
                Throws.ArgumentNullException);
        }
    }
}
