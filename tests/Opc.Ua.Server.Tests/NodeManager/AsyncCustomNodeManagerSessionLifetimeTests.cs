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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Pins awaited, composable session cleanup and its registration/disposal boundaries.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AsyncCustomNodeManagerSessionLifetimeTests
    {
        /// <summary>
        /// Retaining subscriptions does not bypass the base hook or allow an early close response.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ClosingAwaitsRegisteredCleanupAndTheOverride(bool deleteSubscriptions)
        {
            await using Harness harness = CreateHarness();
            var entered = Signal();
            var release = Signal();
            NodeId owner = new("owner", 1);
            using var cancellation = new CancellationTokenSource();
            using IDisposable registration = harness.Manager.RegisterSessionClosingHandler(async (session, token) =>
            {
                Assert.That(session, Is.EqualTo(owner));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });

            Task closing = harness.Manager.SessionClosingAsync(
                null!, owner, deleteSubscriptions, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(harness.Manager.OverrideCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await closing.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            Assert.That(harness.Manager.OverrideCompleted, Is.True);
        }

        /// <summary>
        /// Unregistering inside the callback is nonblocking and removes later invocations.
        /// </summary>
        [Test]
        public async Task AHandlerCanUnregisterItself()
        {
            await using Harness harness = CreateHarness();
            int calls = 0;
            IDisposable? registration = null;
            registration = harness.Manager.RegisterSessionClosingHandler((_, _) =>
            {
                calls++;
                registration!.Dispose();
                return default;
            });
            using (registration)
            {
                await harness.Manager.SessionClosingAsync(null!, new NodeId("one", 1), false);
                await harness.Manager.SessionClosingAsync(null!, new NodeId("two", 1), true);
            }
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Snapshot entries unregistered by an earlier handler are not invoked, and late registrations wait.
        /// </summary>
        [Test]
        public async Task RegistrationChangesDuringDispatchApplyWithoutHoldingTheGate()
        {
            await using Harness harness = CreateHarness();
            int skippedCalls = 0;
            int lateCalls = 0;
            IDisposable? skipped = null;
            IDisposable? late = null;
            using IDisposable first = harness.Manager.RegisterSessionClosingHandler((_, _) =>
            {
                skipped!.Dispose();
                late ??= harness.Manager.RegisterSessionClosingHandler((_, _) =>
                {
                    lateCalls++;
                    return default;
                });
                return default;
            });
            skipped = harness.Manager.RegisterSessionClosingHandler((_, _) =>
            {
                skippedCalls++;
                return default;
            });
            try
            {
                await harness.Manager.SessionClosingAsync(null!, new NodeId("one", 1), false);
                Assert.That(skippedCalls, Is.Zero);
                Assert.That(lateCalls, Is.Zero);
                await harness.Manager.SessionClosingAsync(null!, new NodeId("two", 1), false);
                Assert.That(lateCalls, Is.EqualTo(1));
            }
            finally
            {
                skipped.Dispose();
                late?.Dispose();
            }
        }

        /// <summary>
        /// Disposing a lease does not let session close stop awaiting its already admitted callback.
        /// </summary>
        [Test]
        public async Task UnregisterDoesNotAbandonAnAdmittedCallback()
        {
            await using Harness harness = CreateHarness();
            var entered = Signal();
            var release = Signal();
            int calls = 0;
            using IDisposable registration = harness.Manager.RegisterSessionClosingHandler(async (_, _) =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            Task closing = harness.Manager.SessionClosingAsync(null!, new NodeId("owner", 1), true).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                registration.Dispose();
                Assert.That(closing.IsCompleted, Is.False);
                await harness.Manager.SessionClosingAsync(null!, new NodeId("other", 1), true);
                Assert.That(calls, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await closing.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        /// <summary>
        /// All cleanup is attempted even with a canceled dispatch token and multiple handler failures.
        /// </summary>
        [Test]
        public async Task ClosingAggregatesFailuresAfterAttemptingEveryHandler()
        {
            await using Harness harness = CreateHarness();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            int successfulCalls = 0;
            using IDisposable failed = harness.Manager.RegisterSessionClosingHandler((_, _) =>
                throw new InvalidOperationException("cleanup"));
            using IDisposable canceled = harness.Manager.RegisterSessionClosingHandler((_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return default;
            });
            using IDisposable successful = harness.Manager.RegisterSessionClosingHandler((_, _) =>
            {
                successfulCalls++;
                return default;
            });

            Task closing = harness.Manager.SessionClosingAsync(
                null!, new NodeId("owner", 1), true, cancellation.Token).AsTask();
            await Assert.ThatAsync(async () => await closing, Throws.TypeOf<AggregateException>());

            Assert.That(successfulCalls, Is.EqualTo(1));
            Assert.That(closing.Exception!.Flatten().InnerExceptions, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Manager disposal closes registration admission and joins in-flight dispatch rather than blocking it.
        /// </summary>
        [Test]
        public async Task ManagerDisposalDrainsAnActiveDispatchAndRejectsNewRegistration()
        {
            await using Harness harness = CreateHarness();
            var entered = Signal();
            var release = Signal();
            using IDisposable registration = harness.Manager.RegisterSessionClosingHandler(async (_, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                Assert.That(() => harness.Manager.RegisterSessionClosingHandler((_, _) => default),
                    Throws.TypeOf<ObjectDisposedException>());
            });
            Task closing = harness.Manager.SessionClosingAsync(null!, new NodeId("owner", 1), false).AsTask();
            Task? disposing = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                disposing = harness.Manager.DisposeAsync().AsTask();
                Assert.That(disposing.IsCompleted, Is.False);
                Assert.That(() => harness.Manager.RegisterSessionClosingHandler((_, _) => default),
                    Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                release.TrySetResult(true);
                await closing.WaitAsync(TimeSpan.FromSeconds(10));
                if (disposing != null)
                {
                    await disposing.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            registration.Dispose();
            await harness.Manager.SessionClosingAsync(null!, new NodeId("late", 1), false);
        }

        /// <summary>
        /// Managers with no composed session resources retain the existing no-op base behavior.
        /// </summary>
        [Test]
        public async Task NoRegistrationsPreserveDefaultSessionCloseBehavior()
        {
            await using Harness harness = CreateHarness();
            await harness.Manager.SessionClosingAsync(null!, new NodeId("owner", 1), false);
            await harness.Manager.DisposeAsync();
            await harness.Manager.SessionClosingAsync(null!, new NodeId("owner", 1), true);
            Assert.That(harness.Manager.OverrideCompleted, Is.True);
        }

        /// <summary>
        /// Invalid registration is rejected before touching the registry.
        /// </summary>
        [Test]
        public async Task NullRegistrationIsRejected()
        {
            await using Harness harness = CreateHarness();
            Assert.That(() => harness.Manager.RegisterSessionClosingHandler(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Creates an asynchronous test signal without running continuations under a setter's gate.
        /// </summary>
        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Creates only the standard node manager's required in-memory server dependencies.
        /// </summary>
        private static Harness CreateHarness()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queues = new MonitoredItemQueueFactory(telemetry);
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            server.SetupGet(value => value.NodeManager).Returns(Mock.Of<IMasterNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new Harness(new TestNodeManager(server.Object, telemetry.CreateLogger<TestNodeManager>()), queues);
        }

        /// <summary>
        /// Owns the manager and queue factory for one test.
        /// </summary>
        private sealed class Harness(TestNodeManager manager, MonitoredItemQueueFactory queues) : IAsyncDisposable
        {
            /// <summary>
            /// Gets the manager with a base-delegating session-close override.
            /// </summary>
            public TestNodeManager Manager { get; } = manager;

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync();
                queues.Dispose();
            }
        }

        /// <summary>
        /// Demonstrates the required base delegation for composed resources on a custom owner.
        /// </summary>
        private sealed class TestNodeManager(IServerInternal server, ILogger logger)
            : AsyncCustomNodeManager(server, logger, "urn:tests:session-resources")
        {
            /// <summary>
            /// Gets whether the custom override ran after awaited base cleanup.
            /// </summary>
            public bool OverrideCompleted { get; private set; }

            /// <inheritdoc/>
            public override async ValueTask SessionClosingAsync(
                OperationContext context,
                NodeId sessionId,
                bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
            {
                await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken);
                OverrideCompleted = true;
            }
        }
    }
}
