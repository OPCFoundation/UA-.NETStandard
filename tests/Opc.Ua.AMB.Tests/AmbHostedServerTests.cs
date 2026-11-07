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
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Verifies ownership and failure reporting in the hosted AMB test fixture.
    /// </summary>
    [TestFixture]
    [Category("AMB")]
    [Category("Hosting")]
    public sealed class AmbHostedServerTests
    {
        /// <summary>
        /// An incompatible hosted service is rejected without leaking its provider or storage.
        /// </summary>
        [Test]
        public async Task RejectsNonBackgroundServiceAndCleansUpAsync()
        {
            var hosted = new Mock<IHostedService>();
            Mock<IDisposable> disposable = hosted.As<IDisposable>();
            string? root = null;

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                    nameof(RejectsNonBackgroundServiceAndCleansUpAsync),
                    builder => ConfigureHostedFixture(builder, hosted.Object, path => root = path))
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(failure!.Message, Does.Contain("requires a BackgroundService"));
            Assert.That(root, Is.Not.Null);
            Assert.That(Directory.Exists(root), Is.False);
            disposable.Verify(service => service.Dispose(), Times.Once);
            hosted.Verify(service => service.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
            hosted.Verify(service => service.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A failed service factory still disposes services already resolved by the provider.
        /// </summary>
        [Test]
        public async Task CleansUpWhenHostedServiceResolutionFailsAsync()
        {
            var hosted = new Mock<BackgroundService>();
            var expected = new InvalidOperationException("Hosted service resolution failed.");
            string? root = null;

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                    nameof(CleansUpWhenHostedServiceResolutionFailsAsync),
                    builder =>
                    {
                        ConfigureHostedFixture(builder, hosted.Object, path => root = path);
                        builder.Services.AddSingleton<IHostedService>(_ => throw expected);
                    }).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(root, Is.Not.Null);
            Assert.That(Directory.Exists(root), Is.False);
            hosted.Verify(service => service.Dispose(), Times.Once);
            hosted.Verify(service => service.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
            hosted.Verify(service => service.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A fixture-construction failure cleans up before the hosted service has started.
        /// </summary>
        [Test]
        public async Task CleansUpWhenAssetManagementResolutionFailsAsync()
        {
            var hosted = new Mock<BackgroundService>();
            string? root = null;

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                    nameof(CleansUpWhenAssetManagementResolutionFailsAsync),
                    builder =>
                    {
                        ConfigureHostedFixture(builder, hosted.Object, path => root = path);
                        builder.Services.RemoveAll<AssetManagement>();
                    }).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(failure!.Message, Does.Contain(nameof(AssetManagement)));
            Assert.That(root, Is.Not.Null);
            Assert.That(Directory.Exists(root), Is.False);
            hosted.Verify(service => service.Dispose(), Times.Once);
            hosted.Verify(service => service.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
            hosted.Verify(service => service.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Successful cleanup preserves the original startup exception, including cancellation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreservesStartupFailureAfterSuccessfulCleanupAsync(bool cancelled)
        {
            using var cancellation = new CancellationTokenSource();
            if (cancelled)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            Exception expected = cancelled
                ? new OperationCanceledException(cancellation.Token)
                : new InvalidOperationException("Hosted service startup failed.");
            var hosted = new Mock<BackgroundService>();
            hosted.Setup(service => service.StartAsync(cancellation.Token)).ThrowsAsync(expected);
            hosted.Setup(service => service.StopAsync(CancellationToken.None)).Returns(Task.CompletedTask);
            string? root = null;

            Exception? failure = await Assert.CatchAsync<Exception>(async () =>
            {
                await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                    nameof(PreservesStartupFailureAfterSuccessfulCleanupAsync),
                    builder => ConfigureHostedFixture(builder, hosted.Object, path => root = path),
                    cancellation.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(root, Is.Not.Null);
            Assert.That(Directory.Exists(root), Is.False);
            hosted.Verify(service => service.StopAsync(CancellationToken.None), Times.Once);
            hosted.Verify(service => service.Dispose(), Times.Once);
        }

        /// <summary>
        /// Startup, shutdown, disposal and directory-deletion failures remain observable together.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Platform("Win")]
        public async Task PreservesStartupAndCleanupFailuresAsync(bool failStopAndDispose)
        {
            var startupFailure = new InvalidOperationException("Hosted service startup failed.");
            var stopFailure = new InvalidOperationException("Hosted service shutdown failed.");
            var disposeFailure = new InvalidOperationException("Hosted service disposal failed.");
            var hosted = new Mock<BackgroundService>();
            hosted.Setup(service => service.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(startupFailure);
            if (failStopAndDispose)
            {
                hosted.Setup(service => service.StopAsync(CancellationToken.None)).ThrowsAsync(stopFailure);
                hosted.Setup(service => service.Dispose()).Throws(disposeFailure);
            }
            else
            {
                hosted.Setup(service => service.StopAsync(CancellationToken.None)).Returns(Task.CompletedTask);
            }
            string? root = null;
            string? readOnlyFile = null;
            try
            {
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                {
                    await using AmbHostedServer server = await AmbHostedServer.StartAsync(
                        nameof(PreservesStartupAndCleanupFailuresAsync),
                        builder => ConfigureHostedFixture(builder, hosted.Object, path =>
                        {
                            root = path;
                            readOnlyFile = Path.Combine(path, "read-only");
                            File.WriteAllText(readOnlyFile, string.Empty);
                            File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
                        })).ConfigureAwait(false);
                }).ConfigureAwait(false);

                Assert.That(root, Is.Not.Null);
                Assert.That(Directory.Exists(root), Is.True);
                ReadOnlyCollection<Exception> failures = failure!.Flatten().InnerExceptions;
                Assert.That(failures, Has.Count.EqualTo(failStopAndDispose ? 4 : 2));
                Assert.That(failures, Does.Contain(startupFailure));
                Assert.That(failures[^1], Is.InstanceOf<UnauthorizedAccessException>());
                if (failStopAndDispose)
                {
                    Assert.That(failures, Does.Contain(stopFailure));
                    Assert.That(failures, Does.Contain(disposeFailure));
                }
                hosted.Verify(service => service.StopAsync(CancellationToken.None), Times.Once);
                hosted.Verify(service => service.Dispose(), Times.Once);
            }
            finally
            {
                if (readOnlyFile is not null && File.Exists(readOnlyFile))
                {
                    File.SetAttributes(readOnlyFile, FileAttributes.Normal);
                }
                if (root is not null && Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Registers a provider-owned test service and creates storage during its resolution.
        /// </summary>
        private static void ConfigureHostedFixture(
            IOpcUaServerBuilder builder, IHostedService hostedService, Action<string> initializeStorage)
        {
            builder.AddAssetManagement();
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.AddSingleton(provider =>
            {
                string root = Path.GetDirectoryName(
                    provider.GetRequiredService<IOptions<OpcUaServerOptions>>().Value.PkiRoot)
                    ?? throw new InvalidOperationException("The fixture storage root is missing.");
                Directory.CreateDirectory(root);
                initializeStorage(root);
                return hostedService;
            });
        }
    }
}
