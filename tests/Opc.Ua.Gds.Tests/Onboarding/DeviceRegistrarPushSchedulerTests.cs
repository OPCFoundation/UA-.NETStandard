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
using System.Collections.Generic;
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
        /// Durable startup refuses to retry persisted work unless the processor can reconcile interrupted effects.
        /// </summary>
        [Test]
        public async Task DurableStartupRequiresRecoveryProcessor()
        {
            using var workStore = new Mock<IDeviceRegistrarPushWorkStore>().Object;
            using var scheduler = new DeviceRegistrarPushScheduler(
                new Processor(), NUnitTelemetryContext.Create(), capacity: 32, workStore: workStore);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// A post-publication staging-file inspection failure cannot turn durable acceptance into a reported failure.
        /// </summary>
        [Test]
        public async Task SuccessfulDurablePublicationDoesNotReportStagingInspectionFailure()
        {
            using var files = new VirtualFileSystem();
            const string path = "push-publication.bin";
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(value => value.Exists(
                It.Is<string>(candidate => string.Equals(candidate, path, StringComparison.Ordinal)), false))
                .Returns((string candidate, bool _) => files.Exists(candidate));
            fileSystem.Setup(value => value.Exists(
                It.Is<string>(candidate =>
                    candidate.StartsWith(path + ".", StringComparison.Ordinal) &&
                    candidate.EndsWith(".pending", StringComparison.Ordinal)), false))
                .Throws(new InvalidOperationException("Staging inspection is unavailable."));
            fileSystem.Setup(value => value.OpenRead(It.IsAny<string>()))
                .Returns((string candidate) => files.OpenRead(candidate));
            fileSystem.Setup(value => value.OpenWrite(It.IsAny<string>()))
                .Returns((string candidate) => files.OpenWrite(candidate));
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string source, string destination) => files.Replace(source, destination));
            fileSystem.Setup(value => value.Delete(It.IsAny<string>(), false))
                .Callback((string candidate, bool _) => files.Delete(candidate));

            using var store = new FileSystemDeviceRegistrarPushWorkStore(
                path, NUnitTelemetryContext.Create(), fileSystem.Object);

            await store.EnqueueAsync(Guid.NewGuid(), Application("published"), 1).ConfigureAwait(false);

            ArrayOf<DeviceRegistrarPushWorkItem> items = await store.ReadAsync().ConfigureAwait(false);
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].Application.ApplicationUri, Is.EqualTo("urn:published"));
        }

        /// <summary>
        /// A failed replacement retains the previous work and removes the staged checkpoint.
        /// </summary>
        [Test]
        public async Task FailedPushWorkPublicationPreservesPreviousCheckpointAndRemovesStaging()
        {
            using var files = new VirtualFileSystem();
            const string path = "push-publication-failure.bin";
            var telemetry = NUnitTelemetryContext.Create();
            Guid previousId = Guid.NewGuid();
            using (var original = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files))
            {
                await original.EnqueueAsync(previousId, Application("previous"), 2).ConfigureAwait(false);
            }
            byte[] previous = files.Get(path).ToArray();
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            var publication = new System.IO.IOException("Checkpoint publication unavailable.");
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path)).Throws(publication);
            using var store = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem.Object);

            System.IO.IOException? actual = await Assert.ThrowsAsync<System.IO.IOException>(async () =>
                await store.EnqueueAsync(Guid.NewGuid(), Application("new"), 2).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(publication));
            Assert.That(files.Get(path), Is.EqualTo(previous));
            Assert.That(files.Files, Is.EquivalentTo(new[] { path }));
            ArrayOf<DeviceRegistrarPushWorkItem> items = await store.ReadAsync().ConfigureAwait(false);
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].Id, Is.EqualTo(previousId));
            Assert.That(items[0].State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
            Assert.That(items[0].Application.ApplicationUri, Is.EqualTo("urn:previous"));
            fileSystem.Verify(value => value.Replace(It.IsAny<string>(), path), Times.Once);
            fileSystem.Verify(value => value.Delete(It.IsAny<string>(), false), Times.Once);
        }

        /// <summary>
        /// Publication and staging cleanup failures are both reported without replacing accepted work.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPushWorkPublicationAndCleanupReportBothErrors(bool existsFails)
        {
            using var files = new VirtualFileSystem();
            const string path = "push-publication-cleanup-failure.bin";
            var telemetry = NUnitTelemetryContext.Create();
            Guid previousId = Guid.NewGuid();
            using (var original = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files))
            {
                await original.EnqueueAsync(previousId, Application("previous"), 2).ConfigureAwait(false);
            }
            byte[] previous = files.Get(path).ToArray();
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            var publication = new System.IO.IOException("Checkpoint publication unavailable.");
            var cleanup = new System.IO.IOException("Staging cleanup unavailable.");
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path)).Throws(publication);
            if (existsFails)
            {
                fileSystem.Setup(value => value.Exists(
                    It.Is<string>(candidate => candidate.EndsWith(".pending", StringComparison.Ordinal)), false))
                    .Throws(cleanup);
            }
            else
            {
                fileSystem.Setup(value => value.Delete(It.IsAny<string>(), false)).Throws(cleanup);
            }
            using var store = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem.Object);

            AggregateException? actual = await Assert.ThrowsAsync<AggregateException>(async () =>
                await store.EnqueueAsync(Guid.NewGuid(), Application("new"), 2).ConfigureAwait(false));

            Assert.That(actual!.InnerExceptions, Is.EqualTo(new[] { publication, cleanup }));
            Assert.That(files.Get(path), Is.EqualTo(previous));
            Assert.That(files.Files.Count(), Is.EqualTo(2));
            ArrayOf<DeviceRegistrarPushWorkItem> items = await store.ReadAsync().ConfigureAwait(false);
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].Id, Is.EqualTo(previousId));
            Assert.That(items[0].State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
            Assert.That(items[0].Application.ApplicationUri, Is.EqualTo("urn:previous"));
            fileSystem.Verify(value => value.Replace(It.IsAny<string>(), path), Times.Once);
        }

        /// <summary>
        /// A failed write that published the exact pending item is accepted once by the current scheduler.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedThenThrowingReplacementQueuesConfirmedWorkExactlyOnce(bool cancelAfterPublication)
        {
            using var files = new VirtualFileSystem();
            using var logs = new WorkerLogs();
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(logs);
            const string path = "push-published-then-thrown.bin";
            var publication = new System.IO.IOException("Replacement reported failure after publication.");
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path))
                .Callback((string source, string destination) => files.Replace(source, destination));
            var barrierEntered = Signal();
            var barrierRelease = Signal();
            var firstProcessor = new ScriptedRecoveryProcessor(
                blockedProcessingUri: "urn:worker-barrier",
                processingEntered: barrierEntered,
                processingRelease: barrierRelease);
            Guid publishedId;
            using (var firstStore = new FileSystemDeviceRegistrarPushWorkStore(
                path, telemetry.Object, fileSystem.Object))
            using (var firstScheduler = new DeviceRegistrarPushScheduler(
                firstProcessor, telemetry.Object, capacity: 2, workStore: firstStore))
            {
                await firstScheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await firstScheduler.EnqueueAsync(Application("worker-barrier")).ConfigureAwait(false);
                    await barrierEntered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                    bool failOnce = true;
                    using var cancellation = new CancellationTokenSource();
                    fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path))
                        .Callback((string source, string destination) =>
                        {
                            files.Replace(source, destination);
                            if (failOnce)
                            {
                                failOnce = false;
                                if (cancelAfterPublication)
                                {
                                    cancellation.Cancel();
                                }
                                throw publication;
                            }
                        });
                    await firstScheduler.EnqueueAsync(Application("published-then-thrown"), cancellation.Token)
                        .ConfigureAwait(false);
                    ArrayOf<DeviceRegistrarPushWorkItem> items =
                        await firstStore.ReadAsync().ConfigureAwait(false);
                    Assert.That(items.Count, Is.EqualTo(2));
                    DeviceRegistrarPushWorkItem pending = items.ToList().Single(
                        item => item.Application.ApplicationUri == "urn:published-then-thrown");
                    publishedId = pending.Id;
                    Assert.That(publishedId, Is.Not.EqualTo(Guid.Empty));
                    Assert.That(pending.State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
                    AssertApplicationDescription(
                        pending.Application,
                        "urn:published-then-thrown",
                        "opc.tcp://published-then-thrown:4840");
                    Assert.That(files.Files, Is.EquivalentTo(new[] { path }));
                    Assert.That(firstProcessor.ProcessedApplicationUris, Is.EqualTo(s_workerBarrierApplicationUris));
                    Assert.That(logs.Events.Any(item =>
                        item.Level == LogLevel.Warning && ReferenceEquals(item.Error, publication)), Is.True);
                    barrierRelease.TrySetResult(true);
                    await logs.WaitForCompletionCountAsync(2).ConfigureAwait(false);
                }
                finally
                {
                    barrierRelease.TrySetResult(true);
                    await firstScheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline)
                        .ConfigureAwait(false);
                    if (firstScheduler.ExecuteTask is Task worker)
                    {
                        await worker.WaitAsync(k_deadline).ConfigureAwait(false);
                    }
                }
                Assert.That(firstProcessor.ProcessedApplicationUris,
                    Is.EqualTo(s_publishedApplicationUris));
                Assert.That(firstProcessor.ReconciledApplicationUris, Is.Empty);
                Assert.That((await firstStore.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
            }

            var recoveredProcessor = new ScriptedRecoveryProcessor();
            using var recoveredStore = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry.Object, files);
            ArrayOf<DeviceRegistrarPushWorkItem> restored = await recoveredStore.ReadAsync().ConfigureAwait(false);
            Assert.That(restored.Count, Is.Zero);
            Assert.That(publishedId, Is.Not.EqualTo(Guid.Empty));
            using var recoveredScheduler = new DeviceRegistrarPushScheduler(
                recoveredProcessor, telemetry.Object, capacity: 1, workStore: recoveredStore);
            await recoveredScheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await recoveredScheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            if (recoveredScheduler.ExecuteTask is Task recoveredWorker)
            {
                try
                {
                    await recoveredWorker.WaitAsync(k_deadline).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (recoveredWorker.IsCanceled)
                {
                }
            }
            Assert.That(recoveredProcessor.ProcessedApplicationUris, Is.Empty);
            Assert.That(recoveredProcessor.ReconciledApplicationUris, Is.Empty);
            Assert.That((await recoveredStore.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
        }

        /// <summary>
        /// Only one exact pending record confirms a failed acceptance write; unreadable state retains both errors.
        /// </summary>
        [TestCase("absent")]
        [TestCase("other-id")]
        [TestCase("application")]
        [TestCase("processing")]
        [TestCase("interrupted")]
        [TestCase("duplicate")]
        [TestCase("read-failure")]
        public async Task UnconfirmedPublicationReportsOriginalFailureWithoutQueueing(string scenario)
        {
            var publication = new System.IO.IOException("Acceptance publication failed.");
            var readFailure = new System.IO.IOException("Acceptance confirmation failed.");
            var store = new Mock<IDeviceRegistrarPushWorkStore>(MockBehavior.Strict);
            Guid workId = Guid.Empty;
            bool enqueueAttempted = false;
            store.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    if (!enqueueAttempted)
                    {
                        return new ValueTask<ArrayOf<DeviceRegistrarPushWorkItem>>(
                            ArrayOf<DeviceRegistrarPushWorkItem>.Empty);
                    }
                    Assert.That(token, Is.EqualTo(CancellationToken.None));
                    if (scenario == "read-failure")
                    {
                        throw readFailure;
                    }
                    ApplicationDescription application = Application("unconfirmed");
                    if (scenario == "application")
                    {
                        application.ProductUri = "urn:different-product";
                    }
                    var item = new DeviceRegistrarPushWorkItem(
                        scenario == "other-id" ? Guid.NewGuid() : workId,
                        application,
                        scenario switch
                        {
                            "processing" => DeviceRegistrarPushWorkState.Processing,
                            "interrupted" => DeviceRegistrarPushWorkState.Interrupted,
                            _ => DeviceRegistrarPushWorkState.Pending
                        });
                    ArrayOf<DeviceRegistrarPushWorkItem> items = scenario switch
                    {
                        "absent" => [],
                        "duplicate" => [item, item],
                        _ => [item]
                    };
                    return new ValueTask<ArrayOf<DeviceRegistrarPushWorkItem>>(items);
                });
            store.Setup(value => value.EnqueueAsync(
                    It.IsAny<Guid>(), It.IsAny<ApplicationDescription>(), 1, It.IsAny<CancellationToken>()))
                .Callback((Guid id, ApplicationDescription _, int _, CancellationToken _) =>
                {
                    workId = id;
                    enqueueAttempted = true;
                })
                .Throws(publication);
            var processor = new ScriptedRecoveryProcessor();
            using var scheduler = new DeviceRegistrarPushScheduler(
                processor, NUnitTelemetryContext.Create(), capacity: 1, workStore: store.Object);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (scenario == "read-failure")
                {
                    AggregateException? actual = await Assert.ThrowsAsync<AggregateException>(async () =>
                        await scheduler.EnqueueAsync(Application("unconfirmed")).ConfigureAwait(false));
                    Assert.That(actual!.InnerExceptions, Is.EqualTo(new[] { publication, readFailure }));
                }
                else
                {
                    System.IO.IOException? actual = await Assert.ThrowsAsync<System.IO.IOException>(async () =>
                        await scheduler.EnqueueAsync(Application("unconfirmed")).ConfigureAwait(false));
                    Assert.That(actual, Is.SameAs(publication));
                }
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
                if (scheduler.ExecuteTask is Task worker)
                {
                    await worker.WaitAsync(k_deadline).ConfigureAwait(false);
                }
            }
            Assert.That(workId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(processor.ProcessedApplicationUris, Is.Empty);
            Assert.That(processor.ReconciledApplicationUris, Is.Empty);
            store.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            store.Verify(value => value.EnqueueAsync(
                workId, It.IsAny<ApplicationDescription>(), 1, It.IsAny<CancellationToken>()), Times.Once);
            store.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Disposal during publication lets admitted work queue before closing acceptance and cancelling the worker.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalAfterPublicationAcceptsEnqueueAndLeavesWorkForFreshScheduler(bool publicationThrows)
        {
            using var files = new VirtualFileSystem();
            using var logs = new WorkerLogs();
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(logs);
            const string path = "push-disposed-after-publication.bin";
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            var barrierEntered = Signal();
            var barrierRelease = Signal();
            var firstProcessor = new ScriptedRecoveryProcessor(
                blockedProcessingUri: "urn:disposal-barrier",
                processingEntered: barrierEntered,
                processingRelease: barrierRelease);
            Guid publishedId;
            using (var firstStore = new FileSystemDeviceRegistrarPushWorkStore(
                path, telemetry.Object, fileSystem.Object))
            {
                var firstScheduler = new DeviceRegistrarPushScheduler(
                    firstProcessor, telemetry.Object, capacity: 2, workStore: firstStore);
                bool disposed = false;
                fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path))
                    .Callback((string source, string destination) => files.Replace(source, destination));
                try
                {
                    await firstScheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    await firstScheduler.EnqueueAsync(Application("disposal-barrier")).ConfigureAwait(false);
                    await barrierEntered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                    fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path))
                        .Callback((string source, string destination) =>
                        {
                            files.Replace(source, destination);
                            if (!disposed)
                            {
                                firstScheduler.Dispose();
                                firstScheduler.Dispose();
                                disposed = true;
                                if (publicationThrows)
                                {
                                    throw new System.IO.IOException("Publication completed during disposal.");
                                }
                            }
                        });
                    await firstScheduler.EnqueueAsync(Application("disposed-after-publication"))
                        .ConfigureAwait(false);

                    Assert.That(disposed, Is.True);
                    if (firstScheduler.ExecuteTask is Task completedWorker)
                    {
                        await completedWorker.WaitAsync(k_deadline).ConfigureAwait(false);
                    }
                    ArrayOf<DeviceRegistrarPushWorkItem> items =
                        await firstStore.ReadAsync().ConfigureAwait(false);
                    Assert.That(items.Count, Is.EqualTo(2));
                    DeviceRegistrarPushWorkItem pending = items.ToList().Single(
                        item => item.Application.ApplicationUri == "urn:disposed-after-publication");
                    publishedId = pending.Id;
                    Assert.That(publishedId, Is.Not.EqualTo(Guid.Empty));
                    Assert.That(pending.State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
                    AssertApplicationDescription(
                        pending.Application,
                        "urn:disposed-after-publication",
                        "opc.tcp://disposed-after-publication:4840");
                    Assert.That(files.Files, Is.EquivalentTo(new[] { path }));
                    ServiceResultException? halted = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await firstScheduler.EnqueueAsync(Application("after-disposal")).ConfigureAwait(false));
                    Assert.That(halted!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
                }
                finally
                {
                    firstScheduler.Dispose();
                    if (firstScheduler.ExecuteTask is Task worker)
                    {
                        await worker.WaitAsync(k_deadline).ConfigureAwait(false);
                    }
                }
                Assert.That(firstProcessor.ProcessedApplicationUris, Is.EqualTo(s_disposalBarrierApplicationUris));
                Assert.That(firstProcessor.ProcessingCancellationObserved, Is.True);
                Assert.That(firstProcessor.ReconciledApplicationUris, Is.Empty);
            }

            var recoveredProcessor = new ScriptedRecoveryProcessor();
            using var recoveredStore = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry.Object, files);
            ArrayOf<DeviceRegistrarPushWorkItem> restored = await recoveredStore.ReadAsync().ConfigureAwait(false);
            Assert.That(restored.Count, Is.EqualTo(2));
            DeviceRegistrarPushWorkItem restoredPending = restored.ToList().Single(item => item.Id == publishedId);
            Assert.That(restoredPending.State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
            Assert.That(restored.ToList().Single(
                item => item.Application.ApplicationUri == "urn:disposal-barrier").State,
                Is.EqualTo(DeviceRegistrarPushWorkState.Interrupted));
            using var recoveredScheduler = new DeviceRegistrarPushScheduler(
                recoveredProcessor, telemetry.Object, capacity: 2, workStore: recoveredStore);
            await recoveredScheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await logs.WaitForCompletionCountAsync(2).ConfigureAwait(false);
            }
            finally
            {
                await recoveredScheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline)
                    .ConfigureAwait(false);
                if (recoveredScheduler.ExecuteTask is Task worker)
                {
                    await worker.WaitAsync(k_deadline).ConfigureAwait(false);
                }
            }
            Assert.That(recoveredProcessor.ProcessedApplicationUris,
                Is.EquivalentTo(s_disposalRecoveredApplicationUris));
            Assert.That(recoveredProcessor.ReconciledApplicationUris, Is.EqualTo(s_disposalBarrierApplicationUris));
            Assert.That((await recoveredStore.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
        }

        /// <summary>
        /// Asynchronous shutdown waits for durable acceptance before closing the scheduler.
        /// </summary>
        [Test]
        public async Task StopStartedDuringPublicationWaitsForSuccessfulEnqueue()
        {
            using var files = new VirtualFileSystem();
            const string path = "push-stop-during-publication.bin";
            var telemetry = NUnitTelemetryContext.Create();
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            using var store = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem.Object);
            using var scheduler = new DeviceRegistrarPushScheduler(
                new ScriptedRecoveryProcessor(), telemetry, capacity: 1, workStore: store);
            Task? stopping = null;
            bool stopPendingInsidePublication = false;
            int publications = 0;
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path))
                .Callback((string source, string destination) =>
                {
                    files.Replace(source, destination);
                    if (Interlocked.Increment(ref publications) == 1)
                    {
                        stopping = scheduler.StopAsync(CancellationToken.None);
                        stopPendingInsidePublication = !stopping.IsCompleted;
                    }
                });
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("stop-during-publication")).ConfigureAwait(false);

                Assert.That(stopPendingInsidePublication, Is.True);
                Assert.That(stopping, Is.Not.Null);
                await stopping!.WaitAsync(k_deadline).ConfigureAwait(false);
                Assert.That(stopping!.Status, Is.EqualTo(TaskStatus.RanToCompletion));

                ServiceResultException? halted = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await scheduler.EnqueueAsync(Application("after-stop")).ConfigureAwait(false));
                Assert.That(halted!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            }
            finally
            {
                await (stopping ?? scheduler.StopAsync(CancellationToken.None)).WaitAsync(k_deadline)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Caller cancellation after successful publication does not revoke durable acceptance or processing.
        /// </summary>
        [Test]
        public async Task CallerCancellationAfterPublicationDoesNotRejectDurableAcceptance()
        {
            using var files = new VirtualFileSystem();
            using var logs = new WorkerLogs();
            using var cancellation = new CancellationTokenSource();
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(logs);
            const string path = "push-canceled-after-publication.bin";
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            int publications = 0;
            fileSystem.Setup(value => value.Replace(It.IsAny<string>(), path))
                .Callback((string source, string destination) =>
                {
                    files.Replace(source, destination);
                    if (Interlocked.Increment(ref publications) == 1)
                    {
                        cancellation.Cancel();
                    }
                });
            var entered = Signal();
            var release = Signal();
            var processor = new ScriptedRecoveryProcessor(
                blockedProcessingUri: "urn:canceled-after-publication",
                processingEntered: entered,
                processingRelease: release);
            using var store = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry.Object, fileSystem.Object);
            using var scheduler = new DeviceRegistrarPushScheduler(
                processor, telemetry.Object, capacity: 1, workStore: store);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("canceled-after-publication"), cancellation.Token)
                    .ConfigureAwait(false);

                Assert.That(cancellation.IsCancellationRequested, Is.True);
                await entered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                release.TrySetResult(true);
                await logs.WaitForCompletionCountAsync(1).ConfigureAwait(false);
                Assert.That((await store.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
            Assert.That(processor.ProcessedApplicationUris,
                Is.EqualTo(s_canceledPublicationApplicationUris));
            Assert.That(processor.ProcessingCancellationObserved, Is.False);
            Assert.That(processor.ReconciledApplicationUris, Is.Empty);
        }

        /// <summary>
        /// Cancellation after staging flush preserves the prior checkpoint and removes the uncommitted file.
        /// </summary>
        [Test]
        public async Task CancellationAfterStagingFlushPreservesPreviousCheckpoint()
        {
            using var files = new VirtualFileSystem();
            using var cancellation = new CancellationTokenSource();
            const string path = "push-canceled-before-publication.bin";
            var telemetry = NUnitTelemetryContext.Create();
            Guid previousId = Guid.NewGuid();
            using (var original = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files))
            {
                await original.EnqueueAsync(previousId, Application("previous"), 2).ConfigureAwait(false);
            }
            byte[] previous = files.Get(path).ToArray();
            Mock<IFileSystem> fileSystem = ForwardPushWorkFileSystem(files);
            fileSystem.Setup(value => value.OpenWrite(It.IsAny<string>()))
                .Returns((string staging) =>
                    new CancelAfterFlushStream(files.OpenWrite(staging), cancellation));
            using var store = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem.Object);
            Guid canceledId = Guid.NewGuid();

            OperationCanceledException? actual = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await store.EnqueueAsync(canceledId, Application("canceled"), 2, cancellation.Token)
                    .ConfigureAwait(false));

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(actual!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(files.Get(path), Is.EqualTo(previous));
            Assert.That(files.Files, Is.EquivalentTo(new[] { path }));
            fileSystem.Verify(value => value.Replace(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            fileSystem.Verify(value => value.Delete(It.IsAny<string>(), false), Times.Once);
            ArrayOf<DeviceRegistrarPushWorkItem> current = await store.ReadAsync().ConfigureAwait(false);
            Assert.That(current.Count, Is.EqualTo(1));
            Assert.That(current[0].Id, Is.EqualTo(previousId));
            Assert.That(current[0].Id, Is.Not.EqualTo(canceledId));
            Assert.That(current[0].State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
            Assert.That(current[0].Application.ApplicationUri, Is.EqualTo("urn:previous"));
            using var restarted = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files);
            ArrayOf<DeviceRegistrarPushWorkItem> restored = await restarted.ReadAsync().ConfigureAwait(false);
            Assert.That(restored.Count, Is.EqualTo(1));
            Assert.That(restored[0].Id, Is.EqualTo(previousId));
            Assert.That(restored[0].State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
            Assert.That(restored[0].Application.ApplicationUri, Is.EqualTo("urn:previous"));
        }

        /// <summary>
        /// Canceling startup recovery loading leaves accepted pending work available to a fresh scheduler.
        /// </summary>
        [Test]
        public async Task CanceledRecoveryLoadPreservesPendingWorkForFreshScheduler()
        {
            using var files = new VirtualFileSystem();
            using var cancellation = new CancellationTokenSource();
            var telemetry = NUnitTelemetryContext.Create();
            const string path = "push-canceled-recovery-load.bin";
            Guid acceptedId = Guid.NewGuid();
            using (var seed = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files))
            {
                await seed.EnqueueAsync(acceptedId, Application("accepted-before-start"), 1).ConfigureAwait(false);
            }
            byte[] previous = files.Get(path).ToArray();
            var reading = Signal();
            var releaseRead = Signal();
            using var originalStore = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files);
            var delayedStore = new Mock<IDeviceRegistrarPushWorkStore>(MockBehavior.Strict);
            delayedStore.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => ReadRecoveryAsync(token));
            var originalProcessor = new ScriptedRecoveryProcessor();
            using (var originalScheduler = new DeviceRegistrarPushScheduler(
                originalProcessor, telemetry, capacity: 1, workStore: delayedStore.Object))
            {
                Task startup = originalScheduler.StartAsync(cancellation.Token);
                try
                {
                    await reading.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    OperationCanceledException? actual = await Assert.CatchAsync<OperationCanceledException>(
                        async () => await startup.ConfigureAwait(false));
                    Assert.That(actual!.CancellationToken, Is.EqualTo(cancellation.Token));
                }
                finally
                {
                    releaseRead.TrySetResult(true);
                    await originalScheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline)
                        .ConfigureAwait(false);
                    if (originalScheduler.ExecuteTask is Task worker)
                    {
                        await worker.WaitAsync(k_deadline).ConfigureAwait(false);
                    }
                }
                Assert.That(originalScheduler.ExecuteTask, Is.Null);
                Assert.That(originalProcessor.ProcessedApplicationUris, Is.Empty);
                Assert.That(originalProcessor.ReconciledApplicationUris, Is.Empty);
                delayedStore.Verify(value => value.ReadAsync(cancellation.Token), Times.Once);
                delayedStore.VerifyNoOtherCalls();
                Assert.That(files.Get(path), Is.EqualTo(previous));
                ArrayOf<DeviceRegistrarPushWorkItem> retained =
                    await originalStore.ReadAsync().ConfigureAwait(false);
                Assert.That(retained.Count, Is.EqualTo(1));
                Assert.That(retained[0].Id, Is.EqualTo(acceptedId));
                Assert.That(retained[0].State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
            }

            var entered = Signal();
            var release = Signal();
            var recoveredProcessor = new ScriptedRecoveryProcessor(
                blockedProcessingUri: "urn:accepted-before-start",
                processingEntered: entered,
                processingRelease: release);
            using var logs = new WorkerLogs();
            var recoveredTelemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            recoveredTelemetry.SetupGet(value => value.LoggerFactory).Returns(logs);
            using var recoveredStore = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, files);
            using var recoveredScheduler = new DeviceRegistrarPushScheduler(
                recoveredProcessor, recoveredTelemetry.Object, capacity: 1, workStore: recoveredStore);
            await recoveredScheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await entered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                ArrayOf<DeviceRegistrarPushWorkItem> processing =
                    await recoveredStore.ReadAsync().ConfigureAwait(false);
                Assert.That(processing.Count, Is.EqualTo(1));
                Assert.That(processing[0].Id, Is.EqualTo(acceptedId));
                Assert.That(processing[0].State, Is.EqualTo(DeviceRegistrarPushWorkState.Processing));
                release.TrySetResult(true);
                await logs.WaitForCompletionCountAsync(1).ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await recoveredScheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline)
                    .ConfigureAwait(false);
                if (recoveredScheduler.ExecuteTask is Task worker)
                {
                    await worker.WaitAsync(k_deadline).ConfigureAwait(false);
                }
            }
            Assert.That(recoveredProcessor.ProcessedApplicationUris,
                Is.EqualTo(s_acceptedBeforeStartApplicationUris));
            Assert.That(recoveredProcessor.ReconciledApplicationUris, Is.Empty);
            Assert.That((await recoveredStore.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);

            async ValueTask<ArrayOf<DeviceRegistrarPushWorkItem>> ReadRecoveryAsync(CancellationToken token)
            {
                reading.TrySetResult(true);
                await releaseRead.Task.WaitAsync(token).ConfigureAwait(false);
                return await originalStore.ReadAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A crashed attempt still occupies pending capacity when another restored item runs first.
        /// </summary>
        [Test]
        public async Task RestartCountsPreviouslyProcessingWorkAgainstPendingCapacity()
        {
            using var fileSystem = new VirtualFileSystem();
            var telemetry = NUnitTelemetryContext.Create();
            const string path = "push-restart-capacity.bin";
            Guid pendingId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            Guid interruptedId = Guid.Parse("00000000-0000-0000-0000-000000000002");
            using (var original = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem))
            {
                await original.EnqueueAsync(interruptedId, Application("interrupted"), 1).ConfigureAwait(false);
                await original.BeginAttemptAsync(interruptedId).ConfigureAwait(false);
                await original.EnqueueAsync(pendingId, Application("pending"), 1).ConfigureAwait(false);
            }

            var entered = Signal();
            var release = Signal();
            var processor = new ScriptedRecoveryProcessor(
                blockedProcessingUri: "urn:pending", processingEntered: entered, processingRelease: release);
            using var store = new FileSystemDeviceRegistrarPushWorkStore(path, telemetry, fileSystem);
            using var scheduler = new DeviceRegistrarPushScheduler(processor, telemetry, 1, store);
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await entered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                ArrayOf<DeviceRegistrarPushWorkItem> items = await store.ReadAsync().ConfigureAwait(false);
                Assert.That(items.ToList().Single(item => item.Id == interruptedId).State,
                    Is.EqualTo(DeviceRegistrarPushWorkState.Interrupted));
                ServiceResultException? full = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await scheduler.EnqueueAsync(Application("third")).ConfigureAwait(false));
                Assert.That(full!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(processor.ProcessedApplicationUris, Is.EqualTo(s_pendingApplicationUris));
            }
            finally
            {
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
                release.TrySetResult(true);
            }
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
        /// Durable acceptance survives shutdown and fresh store/scheduler instances reconcile only interrupted work.
        /// </summary>
        [Test]
        public async Task DurableStoreRestartsAndRecoversInterruptedAndPendingWork()
        {
            string checkpointDirectory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Opc.Ua.Gds.Tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(checkpointDirectory);
            string checkpointPath = System.IO.Path.Combine(checkpointDirectory, "push-work.bin");

            var firstEntered = Signal();
            var firstRelease = Signal();
            var reconciliationEntered = Signal();
            var reconciliationRelease = Signal();
            using var logs = new WorkerLogs();
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(value => value.LoggerFactory).Returns(logs);

            FileSystemDeviceRegistrarPushWorkStore? store1 = null;
            FileSystemDeviceRegistrarPushWorkStore? store2 = null;
            FileSystemDeviceRegistrarPushWorkStore? store3 = null;
            DeviceRegistrarPushScheduler? scheduler1 = null;
            DeviceRegistrarPushScheduler? scheduler2 = null;
            var processor1 = new ScriptedRecoveryProcessor(
                blockedProcessingUri: "urn:first",
                processingEntered: firstEntered,
                processingRelease: firstRelease);
            var processor2 = new ScriptedRecoveryProcessor(
                blockedReconciliationUri: "urn:first",
                reconciliationEntered: reconciliationEntered,
                reconciliationRelease: reconciliationRelease);
            bool scheduler1Started = false;
            bool scheduler1Stopped = false;
            bool scheduler2Started = false;
            bool scheduler2Stopped = false;

            try
            {
                store1 = new FileSystemDeviceRegistrarPushWorkStore(checkpointPath, telemetry.Object);
                scheduler1 = new DeviceRegistrarPushScheduler(
                    processor1, telemetry.Object, capacity: 1, workStore: store1);
                await scheduler1.StartAsync(CancellationToken.None).ConfigureAwait(false);
                scheduler1Started = true;

                await scheduler1.EnqueueAsync(Application("first")).ConfigureAwait(false);
                await firstEntered.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                await scheduler1.EnqueueAsync(Application("second")).ConfigureAwait(false);
                ServiceResultException? full = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await scheduler1.EnqueueAsync(Application("third")).ConfigureAwait(false));
                Assert.That(full!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));

                DeviceRegistrarPushWorkItem[] accepted = (await store1.ReadAsync()
                    .ConfigureAwait(false)).Span.ToArray();
                Assert.That(accepted, Has.Length.EqualTo(2));
                DeviceRegistrarPushWorkItem firstAccepted =
                    accepted.Single(item => item.Application.ApplicationUri == "urn:first");
                DeviceRegistrarPushWorkItem secondAccepted =
                    accepted.Single(item => item.Application.ApplicationUri == "urn:second");
                Assert.Multiple(() =>
                {
                    Assert.That(firstAccepted.Id, Is.Not.EqualTo(Guid.Empty));
                    Assert.That(secondAccepted.Id, Is.Not.EqualTo(Guid.Empty));
                    Assert.That(firstAccepted.Id, Is.Not.EqualTo(secondAccepted.Id));
                    Assert.That(firstAccepted.State, Is.EqualTo(DeviceRegistrarPushWorkState.Processing));
                    Assert.That(secondAccepted.State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
                    AssertApplicationDescription(firstAccepted.Application, "urn:first", "opc.tcp://first:4840");
                    AssertApplicationDescription(secondAccepted.Application, "urn:second", "opc.tcp://second:4840");
                });

                Guid firstId = firstAccepted.Id;
                Guid secondId = secondAccepted.Id;
                await scheduler1.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
                scheduler1Stopped = true;

                DeviceRegistrarPushWorkItem[] interrupted = (await store1.ReadAsync()
                    .ConfigureAwait(false)).Span.ToArray();
                DeviceRegistrarPushWorkItem firstInterrupted =
                    interrupted.Single(item => item.Application.ApplicationUri == "urn:first");
                DeviceRegistrarPushWorkItem secondPending =
                    interrupted.Single(item => item.Application.ApplicationUri == "urn:second");
                Assert.Multiple(() =>
                {
                    Assert.That(processor1.ProcessingCancellationObserved, Is.True);
                    Assert.That(processor1.ProcessedApplicationUris, Is.EqualTo(s_firstApplicationUri));
                    Assert.That(interrupted, Has.Length.EqualTo(2));
                    Assert.That(firstInterrupted.Id, Is.EqualTo(firstId));
                    Assert.That(secondPending.Id, Is.EqualTo(secondId));
                    Assert.That(firstInterrupted.State, Is.EqualTo(DeviceRegistrarPushWorkState.Interrupted));
                    Assert.That(secondPending.State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
                    AssertApplicationDescription(firstInterrupted.Application, "urn:first", "opc.tcp://first:4840");
                    AssertApplicationDescription(secondPending.Application, "urn:second", "opc.tcp://second:4840");
                });

                scheduler1.Dispose();
                scheduler1 = null;
                store1.Dispose();
                store1 = null;

                store2 = new FileSystemDeviceRegistrarPushWorkStore(checkpointPath, telemetry.Object);
                DeviceRegistrarPushWorkItem[] restoredBeforeStart = (await store2.ReadAsync()
                    .ConfigureAwait(false)).Span.ToArray();
                DeviceRegistrarPushWorkItem restoredFirst =
                    restoredBeforeStart.Single(item => item.Application.ApplicationUri == "urn:first");
                DeviceRegistrarPushWorkItem restoredSecond =
                    restoredBeforeStart.Single(item => item.Application.ApplicationUri == "urn:second");
                Assert.Multiple(() =>
                {
                    Assert.That(restoredBeforeStart, Has.Length.EqualTo(2));
                    Assert.That(restoredFirst.Id, Is.EqualTo(firstId));
                    Assert.That(restoredSecond.Id, Is.EqualTo(secondId));
                    Assert.That(restoredFirst.State, Is.EqualTo(DeviceRegistrarPushWorkState.Interrupted));
                    Assert.That(restoredSecond.State, Is.EqualTo(DeviceRegistrarPushWorkState.Pending));
                    AssertApplicationDescription(restoredFirst.Application, "urn:first", "opc.tcp://first:4840");
                    AssertApplicationDescription(restoredSecond.Application, "urn:second", "opc.tcp://second:4840");
                });

                scheduler2 = new DeviceRegistrarPushScheduler(
                    processor2, telemetry.Object, capacity: 1, workStore: store2);
                await scheduler2.StartAsync(CancellationToken.None).ConfigureAwait(false);
                scheduler2Started = true;
                await reconciliationEntered.Task.WaitAsync(k_deadline).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(processor2.ProcessedApplicationUris, Does.Not.Contain("urn:first"));
                    Assert.That(processor2.ReconciledApplicationUris, Is.EqualTo(s_firstApplicationUri));
                    Assert.That(processor2.EventsFor("urn:first"),
                        Is.EqualTo(new[] { ("reconcile-enter", "urn:first") }));
                });

                reconciliationRelease.TrySetResult(true);
                await logs.WaitForCompletionCountAsync(2).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(processor2.ProcessedApplicationUris.Count(uri => uri == "urn:first"), Is.EqualTo(1));
                    Assert.That(processor2.ProcessedApplicationUris.Count(uri => uri == "urn:second"), Is.EqualTo(1));
                    Assert.That(processor2.ReconciledApplicationUris, Is.EqualTo(s_firstApplicationUri));
                    Assert.That(processor2.EventsFor("urn:first"),
                        Is.EqualTo(new[] {
                            ("reconcile-enter", "urn:first"),
                            ("reconcile-complete", "urn:first"),
                            ("process", "urn:first")
                        }));
                    Assert.That(processor2.EventsFor("urn:second"),
                        Is.EqualTo(new[] { ("process", "urn:second") }));
                    Assert.That(logs.Events.Count(entry => entry.Event.Id == 100), Is.EqualTo(2));
                });

                await scheduler2.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
                scheduler2Stopped = true;
                scheduler2.Dispose();
                scheduler2 = null;
                store2.Dispose();
                store2 = null;

                store3 = new FileSystemDeviceRegistrarPushWorkStore(checkpointPath, telemetry.Object);
                Assert.That((await store3.ReadAsync().ConfigureAwait(false)).Count, Is.Zero);
            }
            finally
            {
                firstRelease.TrySetResult(true);
                reconciliationRelease.TrySetResult(true);

                if (scheduler1 is not null)
                {
                    try
                    {
                        if (scheduler1Started && !scheduler1Stopped)
                        {
                            await scheduler1.StopAsync(CancellationToken.None)
                                .WaitAsync(k_deadline).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        // Preserve the original test failure while still releasing owned resources.
                    }
                    scheduler1.Dispose();
                }
                if (scheduler2 is not null)
                {
                    try
                    {
                        if (scheduler2Started && !scheduler2Stopped)
                        {
                            await scheduler2.StopAsync(CancellationToken.None)
                                .WaitAsync(k_deadline).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        // Preserve the original test failure while still releasing owned resources.
                    }
                    scheduler2.Dispose();
                }
                store1?.Dispose();
                store2?.Dispose();
                store3?.Dispose();
                if (System.IO.Directory.Exists(checkpointDirectory))
                {
                    System.IO.Directory.Delete(checkpointDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Queue acceptance detaches caller-owned LocalizedText arrays and deferred formatting arguments.
        /// </summary>
        [Test]
        public async Task EnqueueAsyncDetachesLocalizedNamesFromCallerMutation()
        {
            var firstEntered = Signal();
            var releaseFirst = Signal();
            var targetProcessed = Signal();
            var received = new ConcurrentQueue<ApplicationDescription>();
            int processCount = 0;
            var processor = new Processor
            {
                Handler = async (application, cancellationToken) =>
                {
                    received.Enqueue(application);
                    if (Interlocked.Increment(ref processCount) == 1)
                    {
                        firstEntered.TrySetResult(true);
                        await releaseFirst.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        targetProcessed.TrySetResult(true);
                    }
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, NUnitTelemetryContext.Create());
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("blocker")).ConfigureAwait(false);
                await firstEntered.Task.WaitAsync(k_deadline).ConfigureAwait(false);

                object[] formattingArguments = ["Original"];
                var originalName = new LocalizedText(
                    new TranslationInfo("device-name", "en-US", "Device {0}", formattingArguments));
                ApplicationDescription queued = Application("localized");
                queued.ApplicationName = originalName;

                await scheduler.EnqueueAsync(queued).ConfigureAwait(false);
                formattingArguments[0] = "Changed";
                queued.ApplicationName = new LocalizedText("fr-FR", "Caller replacement");
                Assert.Multiple(() =>
                {
                    Assert.That(queued.ApplicationName.Text, Is.EqualTo("Caller replacement"));
                    Assert.That(originalName.Text, Is.EqualTo("Device Changed"));
                });

                releaseFirst.TrySetResult(true);
                await targetProcessed.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                ApplicationDescription processed = received.Single(
                    application => application.ApplicationUri == "urn:localized");
                LocalizedText processedName = processed.ApplicationName;
                Assert.Multiple(() =>
                {
                    Assert.That(received, Has.Count.EqualTo(2));
                    Assert.That(processed.ApplicationUri, Is.EqualTo("urn:localized"));
                    Assert.That(processedName.Locale, Is.EqualTo("en-US"));
                    Assert.That(processedName.Text, Is.EqualTo("Device Original"));
                    Assert.That(processedName.TranslationInfo.Args, Is.EqualTo(new object[] { "Original" }));
                });
            }
            finally
            {
                releaseFirst.TrySetResult(true);
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Queue snapshots do not invoke a provider-owned description's virtual clone or retain its fields.
        /// </summary>
        [Test]
        public async Task EnqueueAsyncDetachesSelfCloningApplicationDescription()
        {
            var firstEntered = Signal();
            var releaseFirst = Signal();
            var targetProcessed = Signal();
            var received = new ConcurrentQueue<ApplicationDescription>();
            int processCount = 0;
            var processor = new Processor
            {
                Handler = async (application, cancellationToken) =>
                {
                    received.Enqueue(application);
                    if (Interlocked.Increment(ref processCount) == 1)
                    {
                        firstEntered.TrySetResult(true);
                        await releaseFirst.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        targetProcessed.TrySetResult(true);
                    }
                }
            };
            using var scheduler = new DeviceRegistrarPushScheduler(processor, NUnitTelemetryContext.Create());
            await scheduler.StartAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await scheduler.EnqueueAsync(Application("blocker")).ConfigureAwait(false);
                await firstEntered.Task.WaitAsync(k_deadline).ConfigureAwait(false);

                string[] discoveryUrls = ["opc.tcp://original:4840"];
                var translations = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["en-US"] = "Original {0}",
                    ["de-DE"] = "Ursprünglich {0}"
                };
                object[] formattingArguments = ["name"];
                var original = new SelfCloningApplicationDescription
                {
                    ApplicationName = new LocalizedText(
                        translations,
                        new TranslationInfo("device-name", "en-US", "Original {0}", formattingArguments)),
                    ApplicationUri = "urn:original",
                    ApplicationType = ApplicationType.Server,
                    ProductUri = "urn:product:original",
                    GatewayServerUri = "urn:gateway:original",
                    DiscoveryProfileUri = "urn:profile:original",
                    DiscoveryUrls = discoveryUrls.ToArrayOf()
                };

                await scheduler.EnqueueAsync(original).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(original.GetType(), Is.EqualTo(typeof(SelfCloningApplicationDescription)));
                    Assert.That(original.ApplicationUri, Is.EqualTo("urn:original"));
                    Assert.That(original.ProductUri, Is.EqualTo("urn:product:original"));
                    Assert.That(discoveryUrls, Is.EqualTo(s_originalDiscoveryUrls));
                    Assert.That(original.ApplicationName.FilterByPreferredLocales(["en-US"]).Text,
                        Is.EqualTo("Original name"));
                });

                original.ApplicationName = new LocalizedText("fr-FR", "Changed");
                original.ApplicationUri = "urn:changed";
                original.ApplicationType = ApplicationType.Client;
                original.ProductUri = "urn:product:changed";
                original.GatewayServerUri = "urn:gateway:changed";
                original.DiscoveryProfileUri = "urn:profile:changed";
                discoveryUrls[0] = "opc.tcp://changed:4840";
                translations["en-US"] = "Changed {0}";
                translations["de-DE"] = "Geändert {0}";
                formattingArguments[0] = "changed";

                releaseFirst.TrySetResult(true);
                await targetProcessed.Task.WaitAsync(k_deadline).ConfigureAwait(false);
                ApplicationDescription processed = received.Single(
                    application => application.ApplicationUri == "urn:original");
                Assert.Multiple(() =>
                {
                    Assert.That(processed.GetType(), Is.EqualTo(typeof(ApplicationDescription)));
                    Assert.That(processed.ApplicationName.Locale, Is.EqualTo("en-US"));
                    Assert.That(processed.ApplicationName.Text, Is.EqualTo("Original name"));
                    Assert.That(processed.ApplicationUri, Is.EqualTo("urn:original"));
                    Assert.That(processed.ApplicationType, Is.EqualTo(ApplicationType.Server));
                    Assert.That(processed.ProductUri, Is.EqualTo("urn:product:original"));
                    Assert.That(processed.GatewayServerUri, Is.EqualTo("urn:gateway:original"));
                    Assert.That(processed.DiscoveryProfileUri, Is.EqualTo("urn:profile:original"));
                    Assert.That(processed.ApplicationName.FilterByPreferredLocales(["en-US"]).Text,
                        Is.EqualTo("Original name"));
                    Assert.That(processed.ApplicationName.FilterByPreferredLocales(["de-DE"]).Text,
                        Is.EqualTo("Ursprünglich name"));
                    Assert.That(processed.ApplicationName.TranslationInfo.Args, Is.EqualTo(new object[] { "name" }));
                    Assert.That(
                        processed.DiscoveryUrls.Span.ToArray(),
                        Is.EqualTo(s_originalDiscoveryUrls));
                });
            }
            finally
            {
                releaseFirst.TrySetResult(true);
                await scheduler.StopAsync(CancellationToken.None).WaitAsync(k_deadline).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Durable acceptance snapshots provider-owned descriptions before persisting their fields and arrays.
        /// </summary>
        [Test]
        public async Task DurableStoreDetachesSelfCloningApplicationDescription()
        {
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "Opc.Ua.Gds.Tests", Guid.NewGuid().ToString("N"));
            string path = System.IO.Path.Combine(directory, "push-work.bin");
            Guid id = Guid.NewGuid();
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                using var store = new FileSystemDeviceRegistrarPushWorkStore(path, NUnitTelemetryContext.Create());
                string[] discoveryUrls = ["opc.tcp://original:4840"];
                var translations = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["en-US"] = "Original {0}",
                    ["de-DE"] = "Ursprünglich {0}"
                };
                object[] formattingArguments = ["name"];
                var original = new SelfCloningApplicationDescription
                {
                    ApplicationName = new LocalizedText(
                        translations,
                        new TranslationInfo("device-name", "en-US", "Original {0}", formattingArguments)),
                    ApplicationUri = "urn:original",
                    ApplicationType = ApplicationType.Server,
                    ProductUri = "urn:product:original",
                    GatewayServerUri = "urn:gateway:original",
                    DiscoveryProfileUri = "urn:profile:original",
                    DiscoveryUrls = discoveryUrls.ToArrayOf()
                };

                await store.EnqueueAsync(id, original, 1).ConfigureAwait(false);
                Assert.That(original.ApplicationUri, Is.EqualTo("urn:original"));
                original.ApplicationUri = "urn:changed";
                discoveryUrls[0] = "opc.tcp://changed:4840";
                translations["en-US"] = "Changed {0}";
                translations["de-DE"] = "Geändert {0}";
                formattingArguments[0] = "changed";

                ArrayOf<DeviceRegistrarPushWorkItem> items = await store.ReadAsync().ConfigureAwait(false);
                Assert.That(items.Count, Is.EqualTo(1));
                ApplicationDescription persisted = items[0].Application;
                Assert.Multiple(() =>
                {
                    Assert.That(persisted.GetType(), Is.EqualTo(typeof(ApplicationDescription)));
                    Assert.That(persisted.ApplicationUri, Is.EqualTo("urn:original"));
                    Assert.That(persisted.ApplicationType, Is.EqualTo(ApplicationType.Server));
                    Assert.That(persisted.ApplicationName.FilterByPreferredLocales(["en-US"]).Text,
                        Is.EqualTo("Original name"));
                    Assert.That(persisted.ProductUri, Is.EqualTo("urn:product:original"));
                    Assert.That(persisted.GatewayServerUri, Is.EqualTo("urn:gateway:original"));
                    Assert.That(persisted.DiscoveryProfileUri, Is.EqualTo("urn:profile:original"));
                    Assert.That(
                        persisted.DiscoveryUrls.Span.ToArray(),
                        Is.EqualTo(s_originalDiscoveryUrls));
                });
            }
            finally
            {
                if (System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Forwards push-work checkpoint operations to an in-memory file system.
        /// </summary>
        private static Mock<IFileSystem> ForwardPushWorkFileSystem(VirtualFileSystem files)
        {
            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(value => value.Exists(It.IsAny<string>(), false))
                .Returns((string path, bool _) => files.Exists(path));
            fileSystem.Setup(value => value.OpenRead(It.IsAny<string>()))
                .Returns((string path) => files.OpenRead(path));
            fileSystem.Setup(value => value.OpenWrite(It.IsAny<string>()))
                .Returns((string path) => files.OpenWrite(path));
            fileSystem.Setup(value => value.Delete(It.IsAny<string>(), false))
                .Callback((string path, bool _) => files.Delete(path));
            return fileSystem;
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
        /// Pins the immutable description fields retained across a durable restart.
        /// </summary>
        private static void AssertApplicationDescription(
            ApplicationDescription application,
            string expectedUri,
            string expectedDiscoveryUrl)
        {
            Assert.That(application.ApplicationUri, Is.EqualTo(expectedUri));
            Assert.That(application.ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(application.DiscoveryUrls.Span.ToArray(), Is.EqualTo(new[] { expectedDiscoveryUrl }));
        }

        /// <summary>
        /// Forwards staging writes and cancels acceptance only after the underlying flush succeeds.
        /// </summary>
        private sealed class CancelAfterFlushStream(
            System.IO.Stream inner,
            CancellationTokenSource cancellation) : System.IO.Stream
        {
            /// <inheritdoc/>
            public override bool CanRead => inner.CanRead;

            /// <inheritdoc/>
            public override bool CanSeek => inner.CanSeek;

            /// <inheritdoc/>
            public override bool CanWrite => inner.CanWrite;

            /// <inheritdoc/>
            public override long Length => inner.Length;

            /// <inheritdoc/>
            public override long Position
            {
                get => inner.Position;
                set => inner.Position = value;
            }

            /// <inheritdoc/>
            public override void Flush()
            {
                inner.Flush();
            }

            /// <inheritdoc/>
            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellation.Cancel();
            }

            /// <inheritdoc/>
            public override int Read(byte[] buffer, int offset, int count)
            {
                return inner.Read(buffer, offset, count);
            }

            /// <inheritdoc/>
            public override long Seek(long offset, System.IO.SeekOrigin origin)
            {
                return inner.Seek(offset, origin);
            }

            /// <inheritdoc/>
            public override void SetLength(long value)
            {
                inner.SetLength(value);
            }

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count)
            {
                inner.Write(buffer, offset, count);
            }

            /// <inheritdoc/>
            public override Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return inner.WriteAsync(buffer, offset, count, cancellationToken);
            }

            /// <inheritdoc/>
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return inner.WriteAsync(buffer, cancellationToken);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (disposing)
                    {
                        inner.Dispose();
                    }
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }

        /// <summary>
        /// Runs scripted processing and recovery while exposing deterministic ordering evidence.
        /// </summary>
        private sealed class ScriptedRecoveryProcessor :
            IDeviceRegistrarPushProcessor,
            IDeviceRegistrarPushRecoveryProcessor
        {
            private readonly string? m_blockedProcessingUri;
            private readonly TaskCompletionSource<bool>? m_processingEntered;
            private readonly TaskCompletionSource<bool>? m_processingRelease;
            private readonly string? m_blockedReconciliationUri;
            private readonly TaskCompletionSource<bool>? m_reconciliationEntered;
            private readonly TaskCompletionSource<bool>? m_reconciliationRelease;
            private readonly ConcurrentQueue<string> m_processedApplicationUris = new();
            private readonly ConcurrentQueue<string> m_reconciledApplicationUris = new();
            private readonly ConcurrentQueue<(string Event, string ApplicationUri)> m_events = new();
            private int m_processingCancellationObserved;

            public ScriptedRecoveryProcessor(
                string? blockedProcessingUri = null,
                TaskCompletionSource<bool>? processingEntered = null,
                TaskCompletionSource<bool>? processingRelease = null,
                string? blockedReconciliationUri = null,
                TaskCompletionSource<bool>? reconciliationEntered = null,
                TaskCompletionSource<bool>? reconciliationRelease = null)
            {
                m_blockedProcessingUri = blockedProcessingUri;
                m_processingEntered = processingEntered;
                m_processingRelease = processingRelease;
                m_blockedReconciliationUri = blockedReconciliationUri;
                m_reconciliationEntered = reconciliationEntered;
                m_reconciliationRelease = reconciliationRelease;
            }

            public bool ProcessingCancellationObserved
                => Volatile.Read(ref m_processingCancellationObserved) != 0;

            public string[] ProcessedApplicationUris
                => m_processedApplicationUris.ToArray();

            public string[] ReconciledApplicationUris
                => m_reconciledApplicationUris.ToArray();

            public (string Event, string ApplicationUri)[] EventsFor(string applicationUri)
            {
                return m_events.Where(item => item.ApplicationUri == applicationUri).ToArray();
            }

            public async ValueTask ProcessAsync(
                ApplicationDescription application,
                CancellationToken cancellationToken = default)
            {
                string applicationUri = application.ApplicationUri!;
                m_processedApplicationUris.Enqueue(applicationUri);
                m_events.Enqueue(("process", applicationUri));
                if (applicationUri == m_blockedProcessingUri)
                {
                    m_processingEntered!.TrySetResult(true);
                    try
                    {
                        await m_processingRelease!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        Interlocked.Exchange(ref m_processingCancellationObserved, 1);
                        throw;
                    }
                }
            }

            public async ValueTask ReconcileInterruptedAsync(
                ApplicationDescription application,
                CancellationToken cancellationToken = default)
            {
                string applicationUri = application.ApplicationUri!;
                m_reconciledApplicationUris.Enqueue(applicationUri);
                m_events.Enqueue(("reconcile-enter", applicationUri));
                if (applicationUri == m_blockedReconciliationUri)
                {
                    m_reconciliationEntered!.TrySetResult(true);
                    await m_reconciliationRelease!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                m_events.Enqueue(("reconcile-complete", applicationUri));
            }
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
            private readonly TaskCompletionSource<bool>[] m_completionSignals = [Signal(), Signal()];
            private int m_completionCount;

            /// <summary>
            /// Gets completed log calls in publication order.
            /// </summary>
            public ConcurrentQueue<(LogLevel Level, EventId Event, Exception? Error, string Message)> Events { get; }
                = new();

            /// <summary>
            /// Waits for the requested number of durable scheduler completion events.
            /// </summary>
            public Task<bool> WaitForCompletionCountAsync(int count)
            {
                if (count < 1 || count > m_completionSignals.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }
                return m_completionSignals[count - 1].Task.WaitAsync(k_deadline);
            }

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
                if (eventId.Id == 100)
                {
                    int count = Interlocked.Increment(ref m_completionCount);
                    if (count <= m_completionSignals.Length)
                    {
                        m_completionSignals[count - 1].TrySetResult(true);
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }
        }

        /// <summary>A failure guard only; test ordering is controlled by explicit signals, not delays.</summary>
        private static readonly TimeSpan k_deadline = TimeSpan.FromSeconds(10);

        /// <summary>The worker-barrier application processed by the scheduler.</summary>
        private static readonly string[] s_workerBarrierApplicationUris = ["urn:worker-barrier"];

        /// <summary>The worker-barrier and published application processed by the scheduler.</summary>
        private static readonly string[] s_publishedApplicationUris =
            ["urn:worker-barrier", "urn:published-then-thrown"];

        /// <summary>The application processed during scheduler disposal.</summary>
        private static readonly string[] s_disposalBarrierApplicationUris = ["urn:disposal-barrier"];

        /// <summary>The applications processed after scheduler disposal and recovery.</summary>
        private static readonly string[] s_disposalRecoveredApplicationUris =
            ["urn:disposal-barrier", "urn:disposed-after-publication"];

        /// <summary>The application accepted after caller cancellation.</summary>
        private static readonly string[] s_canceledPublicationApplicationUris = ["urn:canceled-after-publication"];

        /// <summary>The application retained before startup recovery.</summary>
        private static readonly string[] s_acceptedBeforeStartApplicationUris = ["urn:accepted-before-start"];

        /// <summary>The application processed by the pending-work scenario.</summary>
        private static readonly string[] s_pendingApplicationUris = ["urn:pending"];

        /// <summary>The first application used by durable scheduler recovery.</summary>
        private static readonly string[] s_firstApplicationUri = ["urn:first"];

        /// <summary>The original discovery URL retained by queue snapshots.</summary>
        private static readonly string[] s_originalDiscoveryUrls = ["opc.tcp://original:4840"];

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
