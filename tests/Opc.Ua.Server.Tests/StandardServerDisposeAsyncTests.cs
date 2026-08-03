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

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the deterministic and deferred disposal contracts of
    /// <see cref="StandardServer"/>.
    /// </summary>
    [TestFixture]
    [Category("StandardServer")]
    [NonParallelizable]
    public class StandardServerDisposeAsyncTests
    {
        private sealed class TestableStandardServer : StandardServer
        {
            public TestableStandardServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }
        }

        [Test]
        public async Task DisposeAsyncCompletesAfterServerShutdownAndBaseResourceDisposal()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer server = null;
            var shutdownReachedFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                server.BeforeServerShutdownSemaphoreReleaseForTest = async () =>
                {
                    shutdownReachedFinalRelease.SetResult(null);
                    await allowFinalRelease.Task.ConfigureAwait(false);
                };

                Task disposeTask = server.DisposeAsync().AsTask();

                await shutdownReachedFinalRelease.Task.ConfigureAwait(false);
                Assert.That(disposeTask.IsCompleted, Is.False);
                Assert.That(server.BaseResourcesDisposedForTest, Is.False);

                allowFinalRelease.SetResult(null);
                await disposeTask.ConfigureAwait(false);

                AssertReleased(server);
            }
            finally
            {
                allowFinalRelease.TrySetResult(null);
                await CleanupAsync(fixture, server).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAndDisposeAfterDisposeAsyncIsSafe()
        {
            TestableStandardServer server = CreateServer();

            await server.DisposeAsync().ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);

            Assert.DoesNotThrow(server.Dispose);
            AssertReleased(server);
            Assert.That(server.BaseResourceDisposalCountForTest, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeThenDisposeAsyncJoinsActiveShutdownAndReleasesOnce()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer server = null;
            var shutdownReachedFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                server.BeforeServerShutdownSemaphoreReleaseForTest = async () =>
                {
                    shutdownReachedFinalRelease.SetResult(null);
                    await allowFinalRelease.Task.ConfigureAwait(false);
                };

                server.Dispose();
                await shutdownReachedFinalRelease.Task.ConfigureAwait(false);

                Task disposeAsyncTask = server.DisposeAsync().AsTask();
                Assert.That(disposeAsyncTask.IsCompleted, Is.False);

                allowFinalRelease.SetResult(null);
                await disposeAsyncTask.ConfigureAwait(false);

                AssertReleased(server);
                Assert.That(server.BaseResourceDisposalCountForTest, Is.EqualTo(1));
            }
            finally
            {
                allowFinalRelease.TrySetResult(null);
                await CleanupAsync(fixture, server).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeAsyncThenDisposeIsSafeAndReleasesOnce()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer server = null;
            var shutdownReachedFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                server.BeforeServerShutdownSemaphoreReleaseForTest = async () =>
                {
                    shutdownReachedFinalRelease.SetResult(null);
                    await allowFinalRelease.Task.ConfigureAwait(false);
                };

                Task disposeAsyncTask = server.DisposeAsync().AsTask();
                await shutdownReachedFinalRelease.Task.ConfigureAwait(false);

                Assert.DoesNotThrow(server.Dispose);

                allowFinalRelease.SetResult(null);
                await disposeAsyncTask.ConfigureAwait(false);

                AssertReleased(server);
                Assert.That(server.BaseResourceDisposalCountForTest, Is.EqualTo(1));
            }
            finally
            {
                allowFinalRelease.TrySetResult(null);
                await CleanupAsync(fixture, server).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeAsyncOnNeverStartedServerReleasesCleanly()
        {
            TestableStandardServer server = CreateServer();

            await server.DisposeAsync().ConfigureAwait(false);

            AssertReleased(server);
            Assert.That(server.ServerShutdownAttemptCountForTest, Is.Zero);
        }

        [Test]
        public async Task DisposeRetainsDeferredShutdownCompletion()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer server = null;
            var shutdownReachedFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFinalRelease = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                server.BeforeServerShutdownSemaphoreReleaseForTest = async () =>
                {
                    shutdownReachedFinalRelease.SetResult(null);
                    await allowFinalRelease.Task.ConfigureAwait(false);
                };

                server.Dispose();
                await shutdownReachedFinalRelease.Task.ConfigureAwait(false);

                Assert.That(server.BaseResourcesDisposedForTest, Is.False);

                allowFinalRelease.SetResult(null);
                await WaitUntilAsync(() => server.BaseResourcesDisposedForTest).ConfigureAwait(false);

                AssertReleased(server);
                Assert.That(server.BaseResourceDisposalCountForTest, Is.EqualTo(1));
            }
            finally
            {
                allowFinalRelease.TrySetResult(null);
                await CleanupAsync(fixture, server).ConfigureAwait(false);
            }
        }

        private static ServerFixture<TestableStandardServer> CreateFixture()
        {
            return new ServerFixture<TestableStandardServer>(
                telemetry => new TestableStandardServer(telemetry))
            {
                SecurityNone = true
            };
        }

        private static TestableStandardServer CreateServer()
        {
            return new TestableStandardServer(NUnitTelemetryContext.Create());
        }

        private static void AssertReleased(TestableStandardServer server)
        {
            Assert.That(server.BaseResourcesDisposedForTest, Is.True);
            Assert.That(server.ServerSemaphoreDisposedForTest, Is.True);
            Assert.That(server.DeferredServerShutdownTerminalErrorForTest, Is.Null);
        }

        private static async Task CleanupAsync(
            ServerFixture<TestableStandardServer> fixture,
            TestableStandardServer server)
        {
            if (server is not null && !server.BaseResourcesDisposedForTest)
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }

            IApplicationInstance application = fixture.Application;
            if (application is not null)
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (!predicate())
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    Assert.Fail("The deferred server shutdown did not complete.");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }
}
