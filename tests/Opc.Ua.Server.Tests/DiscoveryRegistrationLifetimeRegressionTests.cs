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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Lds.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies registration timer ownership when discovery requests complete, repeat, or race server shutdown.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("Discovery")]
    [NonParallelizable]
    public sealed class DiscoveryRegistrationLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies that an in-flight discovery registration cannot rearm its timer after the server stops.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistrationCompletionAfterStopNeverRearmsTimerAsync(bool succeeds)
        {
            await using RegistrationHarness harness = await RegistrationHarness.StartAsync(succeeds)
                .ConfigureAwait(false);
            ManualTimer timer = GetRegistrationTimer(harness.Server)!;
            harness.FireRegistration(timer);
            Task registration = harness.RegistrationCompletion;
            try
            {
                Task first = await Task.WhenAny(harness.Entered.Task, registration)
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                if (first == registration)
                {
                    await registration.ConfigureAwait(false);
                    Assert.Fail("Registration did not reach the isolated LDS.");
                }
                await harness.Server.StopAsync(CancellationToken.None).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            finally
            {
                harness.Release.TrySetResult(true);
                await registration.ConfigureAwait(false);
            }
            Assert.That(GetRegistrationTimer(harness.Server), Is.Null);
            Assert.That(timer.Disposed, Is.True);
        }

        /// <summary>
        /// Verifies that a queued timer callback cannot contact discovery or restart scheduling after shutdown.
        /// </summary>
        [Test]
        public async Task QueuedRegistrationCallbackCannotRestartAfterStopAsync()
        {
            await using RegistrationHarness harness = await RegistrationHarness.StartAsync(succeeds: true)
                .ConfigureAwait(false);
            ManualTimer timer = GetRegistrationTimer(harness.Server)!;
            await harness.Server.StopAsync(CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            harness.Release.TrySetResult(true);
            harness.FireRegistration(timer);
            await harness.RegistrationCompletion.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.That(harness.Entered.Task.IsCompleted, Is.False);
            Assert.That(GetRegistrationTimer(harness.Server), Is.Null);
        }

        /// <summary>
        /// Verifies that registration schedules one success or retry timer and ignores the consumed callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ActiveRegistrationRearmsOnceAndIgnoresItsConsumedCallbackAsync(bool succeeds)
        {
            await using RegistrationHarness harness = await RegistrationHarness.StartAsync(succeeds)
                .ConfigureAwait(false);
            ManualTimer original = GetRegistrationTimer(harness.Server)!;
            harness.Release.TrySetResult(true);
            harness.FireRegistration(original);
            await harness.RegistrationCompletion.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            ManualTimer next = GetRegistrationTimer(harness.Server)!;
            Assert.That(next, Is.Not.SameAs(original));
            Assert.That(next.DueTime, Is.EqualTo(TimeSpan.FromMilliseconds(succeeds ? 60_000 : 2_000)));
            Assert.That(original.Disposed, Is.True);
            int calls = harness.RegistrationCalls;
            harness.FireRegistration(original);
            await harness.RegistrationCompletion.ConfigureAwait(false);
            Assert.That(GetRegistrationTimer(harness.Server), Is.SameAs(next));
            Assert.That(harness.RegistrationCalls, Is.EqualTo(calls));
        }

        /// <summary>
        /// Reads the server's current registration timer to check replacement and shutdown state.
        /// </summary>
        private static ManualTimer? GetRegistrationTimer(StandardServer server)
        {
            return (ManualTimer?)s_timerField.GetValue(server);
        }

        /// <summary>
        /// Fires a registration callback and captures completion through its task or asynchronous operation context.
        /// </summary>
        private static Task FireRegistration(StandardServer server, ManualTimer timer)
        {
            var completion = new CallbackCompletionContext();
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(completion);
            try
            {
                timer.Fire();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            return s_taskField?.GetValue(server) as Task ?? completion.Completion;
        }

        /// <summary>
        /// Tracks completion of asynchronous callbacks that do not expose a task directly.
        /// </summary>
        private sealed class CallbackCompletionContext : SynchronizationContext
        {
            /// <summary>
            /// Gets the callback completion task, or a completed task if no asynchronous operation started.
            /// </summary>
            public Task Completion => m_started ? m_completed.Task : Task.CompletedTask;

            /// <summary>
            /// Records that the timer callback started an asynchronous operation.
            /// </summary>
            public override void OperationStarted()
            {
                m_started = true;
            }

            /// <summary>
            /// Signals that the asynchronous callback has finished.
            /// </summary>
            public override void OperationCompleted()
            {
                m_completed.TrySetResult(true);
            }

            /// <summary>
            /// Completes when the asynchronous registration callback leaves the synchronization context.
            /// </summary>
            private readonly TaskCompletionSource<bool> m_completed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Records whether a callback started work whose completion must be awaited.
            /// </summary>
            private bool m_started;
        }

        /// <summary>
        /// Hosts an isolated discovery server and an application server with manually fired registration callbacks.
        /// </summary>
        private sealed class RegistrationHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates a discovery store whose registration can be held and then completed or failed.
            /// </summary>
            private RegistrationHarness(bool succeeds)
            {
                m_store = new RegisteredServerStore();
                var store = new Mock<IRegisteredServerStore>();
                store.Setup(value => value.Snapshot()).Returns(() => m_store.Snapshot());
                store.Setup(value => value.SnapshotNetworkRecords()).Returns(() => m_store.SnapshotNetworkRecords());
                store.Setup(value => value.Find(It.IsAny<ICollection<string>>(), It.IsAny<ICollection<string>>()))
                    .Returns((ICollection<string> uris, ICollection<string> locales) => m_store.Find(uris, locales));
                store.Setup(value => value.RegisterAsync(
                        It.IsAny<RegisteredServer>(), It.IsAny<MdnsDiscoveryConfiguration>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (RegisteredServer server, MdnsDiscoveryConfiguration mdns, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_registrationCalls);
                        Entered.TrySetResult(true);
                        await Release.Task.ConfigureAwait(false);
                        if (!succeeds)
                        {
                            throw new ServiceResultException(StatusCodes.BadInternalError);
                        }
                        return await m_store.RegisterAsync(server, mdns, ct).ConfigureAwait(false);
                    });
                m_discovery = new ServerFixture<LdsServer>(telemetry => new LdsServer(telemetry, store.Object))
                {
                    AutoAccept = true
                };
                m_application = new ServerFixture<StandardServer>(
                    telemetry => new StandardServer(telemetry, new ManualClock()))
                {
                    AutoAccept = true
                };
            }

            /// <summary>
            /// Gets the application server whose registration lifetime is under test.
            /// </summary>
            public StandardServer Server => m_application.Server;

            /// <summary>
            /// Gets the signal raised when registration reaches the discovery store.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the gate that allows a blocked discovery registration to complete.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets completion of the most recently fired registration callback.
            /// </summary>
            public Task RegistrationCompletion => m_registrationTask ?? Task.CompletedTask;

            /// <summary>
            /// Gets the number of registration requests observed by the discovery store.
            /// </summary>
            public int RegistrationCalls => Volatile.Read(ref m_registrationCalls);

            /// <summary>
            /// Fires the supplied registration timer and retains its completion task.
            /// </summary>
            public void FireRegistration(ManualTimer timer)
            {
                m_registrationTask = DiscoveryRegistrationLifetimeRegressionTests.FireRegistration(Server, timer);
            }

            /// <summary>
            /// Starts both servers with a controlled registration outcome and an encrypted discovery endpoint.
            /// </summary>
            public static async Task<RegistrationHarness> StartAsync(bool succeeds)
            {
                var harness = new RegistrationHarness(succeeds);
                bool started = false;
                try
                {
                    LdsServer lds = await harness.m_discovery.StartAsync().ConfigureAwait(false);
                    EndpointDescription endpoint = lds.GetEndpoints().ToArray()!.First(value =>
                        value.SecurityPolicyUri == SecurityPolicies.Basic256Sha256 &&
                        value.SecurityMode == MessageSecurityMode.SignAndEncrypt);
                    await harness.m_application.LoadConfigurationAsync().ConfigureAwait(false);
                    ServerConfiguration configuration = harness.m_application.Config.ServerConfiguration!;
                    configuration.MaxRegistrationInterval = 60_000;
                    configuration.RegistrationEndpoint = endpoint;
                    await harness.m_application.StartAsync().ConfigureAwait(false);
                    started = true;
                    return harness;
                }
                finally
                {
                    if (!started)
                    {
                        await harness.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Releases pending registration, drains its callback, and stops both servers before disposing the store.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                Release.TrySetResult(true);
                if (m_registrationTask != null)
                {
                    await m_registrationTask.ConfigureAwait(false);
                }
                await m_application.StopAsync().ConfigureAwait(false);
                await m_discovery.StopAsync().ConfigureAwait(false);
                m_store.Dispose();
            }

            /// <summary>
            /// Retains the discovery registrations behind the controlled store wrapper.
            /// </summary>
            private readonly RegisteredServerStore m_store;

            /// <summary>
            /// Hosts the discovery endpoint receiving the controlled registration.
            /// </summary>
            private readonly ServerFixture<LdsServer> m_discovery;

            /// <summary>
            /// Hosts the application whose registration and shutdown lifetimes are exercised.
            /// </summary>
            private readonly ServerFixture<StandardServer> m_application;

            /// <summary>
            /// Tracks the latest manually fired registration callback until it finishes.
            /// </summary>
            private Task? m_registrationTask;

            /// <summary>
            /// Counts requests reaching the controlled discovery store.
            /// </summary>
            private int m_registrationCalls;
        }

        /// <summary>
        /// Uses real clock readings while returning timers that run only when explicitly fired.
        /// </summary>
        private sealed class ManualClock : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

            /// <inheritdoc/>
            public override long GetTimestamp() => Stopwatch.GetTimestamp();

            /// <summary>
            /// Creates a manually fired timer carrying the requested callback and initial due time.
            /// </summary>
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                return new ManualTimer(callback, state, dueTime);
            }
        }

        /// <summary>
        /// Captures registration scheduling and permits explicit delivery of queued or consumed callbacks.
        /// </summary>
        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            /// <summary>
            /// Gets whether the owner has disposed the timer.
            /// </summary>
            public bool Disposed { get; private set; }

            /// <summary>
            /// Gets the most recently requested delay before registration.
            /// </summary>
            public TimeSpan DueTime { get; private set; } = dueTime;

            /// <summary>
            /// Records the requested due time and reports whether the timer is still live.
            /// </summary>
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueTime = dueTime;
                return !Disposed;
            }

            /// <summary>
            /// Delivers the captured callback even after disposal to simulate an already queued invocation.
            /// </summary>
            public void Fire() => callback(state);

            /// <summary>
            /// Marks the timer as disposed without discarding the captured callback.
            /// </summary>
            public void Dispose()
            {
                Disposed = true;
            }

            /// <summary>
            /// Marks the timer as disposed and completes without asynchronous work.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }

        /// <summary>
        /// Locates the active registration timer for ownership and scheduling assertions.
        /// </summary>
        private static readonly FieldInfo s_timerField = typeof(StandardServer)
            .GetField("m_registrationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>
        /// Locates a tracked registration task when the server implementation exposes one.
        /// </summary>
        private static readonly FieldInfo? s_taskField = typeof(StandardServer)
            .GetField("m_registrationTask", BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
