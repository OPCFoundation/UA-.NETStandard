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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the deterministic synchronous and asynchronous disposal contracts of
    /// <see cref="StandardServer"/>.
    /// </summary>
    [TestFixture]
    [Category("StandardServer")]
    [NonParallelizable]
    public class StandardServerDisposeAsyncTests
    {
        private sealed class TestableStandardServer : StandardServer
        {
            private sealed class FailOnceRequestQueue(TestableStandardServer server)
                : RequestQueue(server, 1, 1, 1)
            {
                private int m_failureRemaining = 1;

                protected override void Dispose(bool disposing)
                {
                    if (disposing && Interlocked.Exchange(ref m_failureRemaining, 0) != 0)
                    {
                        throw new InvalidOperationException("The request queue drain failed.");
                    }
                    base.Dispose(disposing);
                }
            }

            private int m_serverStoppingCount;

            public TestableStandardServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            /// <summary>
            /// The number of times the orderly stop sequence entered
            /// <see cref="OnServerStoppingAsync"/>.
            /// </summary>
            public int ServerStoppingCount => Volatile.Read(ref m_serverStoppingCount);

            /// <summary>
            /// Gets or sets whether to report a failure after the server stop stages complete.
            /// </summary>
            public bool ThrowAfterStopping { get; set; }

            public async Task FailNextRequestQueueStopAsync()
            {
                FieldInfo? requestQueueField = null;
                for (Type? currentType = typeof(StandardServer);
                    currentType is not null && requestQueueField is null;
                    currentType = currentType.BaseType)
                {
                    requestQueueField = currentType.GetField(
                        "m_requestQueue",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
                FieldInfo resolvedRequestQueueField = requestQueueField
                    ?? throw new AssertionException("The request queue field was not found.");
                if (resolvedRequestQueueField.GetValue(this) is RequestQueue requestQueue)
                {
                    await requestQueue.StopAsync().ConfigureAwait(false);
                }
                resolvedRequestQueueField.SetValue(this, new FailOnceRequestQueue(this));
            }

            protected override async ValueTask OnServerStoppingAsync(
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_serverStoppingCount);
                await base.OnServerStoppingAsync(cancellationToken).ConfigureAwait(false);
                if (ThrowAfterStopping)
                {
                    throw new InvalidOperationException("The stop hook failed after completing shutdown.");
                }
            }
        }

        [Test]
        public async Task DisposeAsyncDisposesBaseResources()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);

                await server.DisposeAsync().ConfigureAwait(false);

                AssertReleased(server);
            }
            finally
            {
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
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
        }

        [Test]
        public async Task ConcurrentDisposeAndDisposeAsyncBothCompleteAndReleaseOnce()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);

                Task disposeTask = Task.Run(server.Dispose);
                Task disposeAsyncTask = server.DisposeAsync().AsTask();

                await Task.WhenAll(disposeTask, disposeAsyncTask).ConfigureAwait(false);

                AssertReleased(server);
            }
            finally
            {
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeAsyncThenDisposeIsSafeAndReleasesOnce()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);

                await server.DisposeAsync().ConfigureAwait(false);
                Assert.DoesNotThrow(server.Dispose);

                AssertReleased(server);
            }
            finally
            {
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeAsyncOnNeverStartedServerReleasesCleanly()
        {
            TestableStandardServer server = CreateServer();

            await server.DisposeAsync().ConfigureAwait(false);

            AssertReleased(server);
        }

        /// <summary>
        /// Verifies disposal releases resources when a post-shutdown error is reported.
        /// </summary>
        [Test]
        public async Task DisposeAsyncReleasesResourcesWhenStopReportsFailureAfterTeardownAsync()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                server.ThrowAfterStopping = true;

                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => server.DisposeAsync().AsTask()).ConfigureAwait(false);

                Assert.That(
                    failure!.Message,
                    Is.EqualTo("The stop hook failed after completing shutdown."));
                Assert.That(server.BaseResourcesDisposedForTest, Is.True);
                Assert.That(
                    () => _ = server.CurrentInstance,
                    Throws.TypeOf<ServiceResultException>());
            }
            finally
            {
                if (server is not null)
                {
                    server.ThrowAfterStopping = false;
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Failed request admission drain retains base resources and disposal can retry the drain.
        /// </summary>
        [Test]
        public async Task DisposeAsyncRetainsResourcesWhenRequestQueueDrainFailsAndRetriesAsync()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                await server.FailNextRequestQueueStopAsync().ConfigureAwait(false);

                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                    () => server.DisposeAsync().AsTask()).ConfigureAwait(false);

                Assert.That(failure!.ToString(), Does.Contain("The request queue drain failed."));
                Assert.That(server.CurrentInstance, Is.Not.Null);

                await server.DisposeAsync().ConfigureAwait(false);

                AssertReleased(server);
            }
            finally
            {
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DisposeBlocksUntilShutdownCompletionAndReleasesOnce()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);

                await Task.Run(server.Dispose).ConfigureAwait(false);

                AssertReleased(server);
            }
            finally
            {
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConcurrentAndRepeatStopAsyncRunTeardownOnce()
        {
            ServerFixture<TestableStandardServer> fixture = CreateFixture();
            TestableStandardServer? server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);

                // Concurrent callers must join the single shared stop task.
                Task stopA = server.StopAsync().AsTask();
                Task stopB = server.StopAsync().AsTask();
                await Task.WhenAll(stopA, stopB).ConfigureAwait(false);

                // A later caller joins the same completed stop task and does not re-run teardown.
                await server.StopAsync().ConfigureAwait(false);

                Assert.That(server.ServerStoppingCount, Is.EqualTo(1));
            }
            finally
            {
                await CleanupAsync(fixture, server!).ConfigureAwait(false);
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
    }
}
