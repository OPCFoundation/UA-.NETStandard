/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Checks bounded acceptance, isolated descriptions, observable failure, and cooperative host shutdown.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarPushSchedulerTests
    {
        /// <summary>
        /// Constructor dependencies cannot be replaced by an accepting no-op processor or missing diagnostics.
        /// </summary>
        [Test]
        public void ConstructorRejectsMissingDependencies()
        {
            Assert.That(() => new DeviceRegistrarPushScheduler(null!, NUnitTelemetryContext.Create()),
                Throws.ArgumentNullException);
            Assert.That(() => new DeviceRegistrarPushScheduler(new Processor(), null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Capacity is explicit and bounded before any worker is started.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1025)]
        public void ConstructorRejectsInvalidCapacity(int capacity)
        {
            Assert.That(() => new DeviceRegistrarPushScheduler(
                new Processor(), NUnitTelemetryContext.Create(), capacity),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Both supported capacity limits construct without allocating a worker.
        /// </summary>
        [TestCase(1)]
        [TestCase(1024)]
        public void ConstructorAcceptsCapacityBoundaries(int capacity)
        {
            using var scheduler = new DeviceRegistrarPushScheduler(
                new Processor(), NUnitTelemetryContext.Create(), capacity);
            Assert.That(scheduler, Is.InstanceOf<IDeviceRegistrarPushScheduler>());
        }

        /// <summary>
        /// A stopped or not-yet-started queue cannot return successful acceptance.
        /// </summary>
        [Test]
        public async Task AcceptanceRequiresRunningWorkerAndRestartIsRejected()
        {
            using var scheduler = new DeviceRegistrarPushScheduler(new Processor(), NUnitTelemetryContext.Create());
            ServiceResultException? before = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scheduler.EnqueueAsync(Application()).ConfigureAwait(false));
            Assert.That(before!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));

            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false));
            await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);

            ServiceResultException? after = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scheduler.EnqueueAsync(Application()).ConfigureAwait(false));
            Assert.That(after!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// Startup cancellation leaves the queue unstarted rather than consuming its one permitted start.
        /// </summary>
        [Test]
        public async Task CanceledStartDoesNotOpenAcceptanceOrConsumeStartup()
        {
            using var scheduler = new DeviceRegistrarPushScheduler(new Processor(), NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await scheduler.StartAsync(cancellation.Token).ConfigureAwait(false));
            ServiceResultException? halted = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await scheduler.EnqueueAsync(Application()).ConfigureAwait(false));
            Assert.That(halted!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));

            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
        }

        /// <summary>
        /// A full queue rejects immediately while accepted work retains independent nested values.
        /// </summary>
        [Test]
        public async Task QueueBoundsPendingWorkAndProcessesDetachedDescriptionsSerially()
        {
            var entered = Signal();
            var release = Signal();
            var completed = Signal();
            var received = new ConcurrentQueue<ApplicationDescription>();
            int running = 0;
            var processor = new Processor
            {
                Handler = async (application, ct) =>
                {
                    Assert.That(Interlocked.Increment(ref running), Is.EqualTo(1));
                    try
                    {
                        received.Enqueue(application);
                        if (received.Count == 1)
                        {
                            entered.TrySetResult(true);
                            await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        }
                        else
                        {
                            completed.TrySetResult(true);
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref running);
                    }
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, NUnitTelemetryContext.Create(), 1);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("first")).ConfigureAwait(false);
                await entered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                ApplicationDescription queued = Application("second");
                string[] urls = ["opc.tcp://second:4840"];
                queued.DiscoveryUrls = urls;
                await scheduler.EnqueueAsync(queued).ConfigureAwait(false);
                queued.ApplicationUri = "urn:changed";
                urls[0] = "opc.tcp://changed:4840";

                ServiceResultException? full = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await scheduler.EnqueueAsync(Application("third")).ConfigureAwait(false));
                Assert.That(full!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(received, Has.Count.EqualTo(1));
                release.TrySetResult(true);
                await completed.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                ApplicationDescription[] calls = received.ToArray();
                Assert.That(calls.Select(call => call.ApplicationUri), Is.EqualTo(s_expectedAcceptedApplicationUris));
                Assert.That(calls[1].DiscoveryUrls[0], Is.EqualTo("opc.tcp://second:4840"));
                Assert.That(calls[1], Is.Not.SameAs(queued));
            }
            finally
            {
                release.TrySetResult(true);
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed provider invocation is logged once; processing continues without retrying its remote effects.
        /// </summary>
        [TestCase("service")]
        [TestCase("cancellation")]
        public async Task ProcessorFailureIsObservedWithoutRetryAndNextRegistrationRuns(string scenario)
        {
            using var logs = new WorkerLogs();
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(logs);
            var completed = Signal();
            var calls = new ConcurrentQueue<string?>();
            Exception failure = scenario == "cancellation"
                ? new OperationCanceledException()
                : new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            var processor = new Processor
            {
                Handler = (application, _) =>
                {
                    calls.Enqueue(application.ApplicationUri);
                    if (application.ApplicationUri == "urn:rejected")
                    {
                        throw failure;
                    }
                    completed.TrySetResult(true);
                    return default;
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, telemetry.Object);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("rejected")).ConfigureAwait(false);
                await scheduler.EnqueueAsync(Application("accepted")).ConfigureAwait(false);
                await completed.Task.WaitAsync(k_deadline).ConfigureAwait(false);
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
            Assert.That(calls.ToArray(), Is.EqualTo(s_expectedProcessedApplicationUris));
            var failures = logs.Events.Where(entry => entry.Event.Id == 101).ToArray();
            Assert.That(failures, Has.Length.EqualTo(1));
            Assert.That(failures[0].Level, Is.EqualTo(LogLevel.Error));
            Assert.That(failures[0].Error, Is.SameAs(failure));
        }

        /// <summary>
        /// Shutdown cancels the running processor, never dispatches queued work, and reports discarded acceptance.
        /// </summary>
        [Test]
        public async Task ShutdownCancelsRunningWorkAndReportsUnprocessedRegistrations()
        {
            using var logs = new WorkerLogs();
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(logs);
            var entered = Signal();
            int calls = 0;
            CancellationToken processingToken = default;
            var processor = new Processor
            {
                Handler = async (_, ct) =>
                {
                    Interlocked.Increment(ref calls);
                    processingToken = ct;
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, telemetry.Object);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("running")).ConfigureAwait(false);
                await entered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                await scheduler.EnqueueAsync(Application("queued")).ConfigureAwait(false);
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
            Assert.That(processingToken.IsCancellationRequested, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(logs.Events.Count(entry => entry.Event.Id == 102), Is.EqualTo(1));
            Assert.That(logs.Events.Count(entry => entry.Event.Id == 103), Is.EqualTo(1));
            Assert.That(logs.Events.Single(entry => entry.Event.Id == 103).Message,
                Does.Contain("1 queued registrations"));
        }

        /// <summary>
        /// Cancellation rejects unaccepted work but does not revoke a registration that was already queued.
        /// </summary>
        [Test]
        public async Task CallerCancellationOnlyGovernsQueueAcceptance()
        {
            var entered = Signal();
            var release = Signal();
            var completed = Signal();
            int calls = 0;
            var processor = new Processor
            {
                Handler = async (_, ct) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        Assert.That(ct.IsCancellationRequested, Is.False);
                        completed.TrySetResult(true);
                    }
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("running")).ConfigureAwait(false);
                await entered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                await scheduler.EnqueueAsync(Application("queued"), cancellation.Token).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await scheduler.EnqueueAsync(Application("canceled"), cancellation.Token).ConfigureAwait(false));
                release.TrySetResult(true);
                await completed.Task.WaitAsync(k_deadline).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>
        /// Invalid discovery descriptions never reach the processor.
        /// </summary>
        [TestCase("missing-uri")]
        [TestCase("missing-endpoints")]
        [TestCase("relative-endpoint")]
        [TestCase("client")]
        public async Task InvalidRegistrationIsRejectedBeforeDispatch(string scenario)
        {
            int calls = 0;
            var processor = new Processor
            {
                Handler = (_, _) =>
                {
                    calls++;
                    return default;
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, NUnitTelemetryContext.Create());
            ApplicationDescription application = Application();
            switch (scenario)
            {
                case "missing-uri":
                    application.ApplicationUri = string.Empty;
                    break;
                case "missing-endpoints":
                    application.DiscoveryUrls = [];
                    break;
                case "relative-endpoint":
                    application.DiscoveryUrls = ["relative"];
                    break;
                case "client":
                    application.ApplicationType = ApplicationType.Client;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await scheduler.EnqueueAsync(application).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
            Assert.That(calls, Is.Zero);
        }

        /// <summary>
        /// DI uses the same queue for runtime acceptance and hosted lifecycle without starting during resolution.
        /// </summary>
        [Test]
        public void FluentRegistrationSharesHostedWorkerAndRejectsConflictingSchedulers()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            Assert.That(services.AddDeviceRegistrarPushScheduler<Processor>(1), Is.SameAs(services));
            using ServiceProvider provider = services.BuildServiceProvider();
            var scheduler = provider.GetRequiredService<DeviceRegistrarPushScheduler>();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarPushScheduler>(), Is.SameAs(scheduler));
            Assert.That(provider.GetServices<IHostedService>().Single(), Is.SameAs(scheduler));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarPushProcessor>(), Is.TypeOf<Processor>());
            Assert.That(() => services.AddDeviceRegistrarPushScheduler<Processor>(),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Invalid DI inputs do not add partially configured services.
        /// </summary>
        [Test]
        public void FluentRegistrationRejectsMissingServicesAndInvalidCapacity()
        {
            Assert.That(() => OpcUaDeviceRegistrarBuilderExtensions
                .AddDeviceRegistrarPushScheduler<Processor>(null!), Throws.ArgumentNullException);
            var services = new ServiceCollection();
            Assert.That(() => services.AddDeviceRegistrarPushScheduler<Processor>(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => services.AddDeviceRegistrarPushScheduler<Processor>(1025),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(services, Is.Empty);
        }

        /// <summary>
        /// Provider conflicts reject registration before any descriptor is added or an existing provider is replaced.
        /// </summary>
        [TestCase("scheduler")]
        [TestCase("processor")]
        [TestCase("worker")]
        public void FluentRegistrationPreservesExistingProvidersOnConflict(string scenario)
        {
            var services = new ServiceCollection();
            using var worker = new DeviceRegistrarPushScheduler(new Processor(), NUnitTelemetryContext.Create());
            switch (scenario)
            {
                case "scheduler":
                    services.AddSingleton(new Mock<IDeviceRegistrarPushScheduler>().Object);
                    break;
                case "processor":
                    services.AddSingleton<IDeviceRegistrarPushProcessor>(new Processor());
                    break;
                case "worker":
                    services.AddSingleton(worker);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            ServiceDescriptor original = services.Single();
            Assert.That(() => services.AddDeviceRegistrarPushScheduler<Processor>(), Throws.InvalidOperationException);
            Assert.That(services, Has.Count.EqualTo(1));
            Assert.That(services.Single(), Is.SameAs(original));
        }

        /// <summary>
        /// Creates a syntactically valid description without relying on DNS or a live server.
        /// </summary>
        private static ApplicationDescription Application(string name = "device")
        {
            return new ApplicationDescription
            {
                ApplicationUri = "urn:" + name,
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://" + name + ":4840"]
            };
        }

        /// <summary>
        /// Creates a deterministic handshake without running continuations inside queue operations.
        /// </summary>
        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Supplies a scripted processor without constructing network clients.
        /// </summary>
        private sealed class Processor : IDeviceRegistrarPushProcessor
        {
            /// <summary>
            /// Gets or sets the script for one attempt.
            /// </summary>
            public Func<ApplicationDescription, CancellationToken, ValueTask> Handler { get; set; }
                = static (_, _) => default;

            /// <inheritdoc/>
            public ValueTask ProcessAsync(ApplicationDescription application, CancellationToken cancellationToken)
            {
                return Handler(application, cancellationToken);
            }
        }

        /// <summary>
        /// Records the worker's source-generated diagnostics without writing to external logs.
        /// </summary>
        private sealed class WorkerLogs : ILoggerFactory, ILogger
        {
            /// <summary>
            /// Gets completed log calls in publication order.
            /// </summary>
            public ConcurrentQueue<(LogLevel Level, EventId Event, Exception? Error, string Message)> Events { get; }
                = new();

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return this;
            }

            /// <inheritdoc/>
            public void AddProvider(ILoggerProvider provider)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Events.Enqueue((logLevel, eventId, exception, formatter(state, exception)));
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }
        }

        /// <summary>A failure guard only; test ordering is controlled by explicit signals, not delays.</summary>
        private static readonly TimeSpan k_deadline = TimeSpan.FromSeconds(10);
        /// <summary>
        /// The registrar URIs accepted and processed by the queue-capacity test.
        /// </summary>
        private static readonly string[] s_expectedAcceptedApplicationUris = ["urn:first", "urn:second"];
        /// <summary>
        /// The registrar URIs processed when the first handler invocation fails.
        /// </summary>
        private static readonly string[] s_expectedProcessedApplicationUris = ["urn:rejected", "urn:accepted"];
    }
}
