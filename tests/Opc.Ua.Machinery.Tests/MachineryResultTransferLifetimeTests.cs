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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Exercises transfer ownership at provider, registration, deletion and shutdown barriers.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    [Parallelizable(ParallelScope.All)]
    public sealed class MachineryResultTransferLifetimeTests
    {
        /// <summary>
        /// Closing an owner cannot return until deletion finishes, even when subscriptions are retained.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ClosingAwaitsDeletionBeforeReleasingCapacity(bool deleteSubscriptions)
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            GeneratedFile file = await harness.GenerateAsync(harness.FirstContext);
            var entered = Signal();
            var release = Signal();
            harness.Manager.RemovingFile = async _ =>
            {
                entered.TrySetResult(true);
                await release.Task;
            };
            Task closing = harness.CloseSessionAsync(deleteSubscriptions);
            try
            {
                await entered.Task.WaitAsync(s_timeout);
                Assert.That(closing.IsCompleted, Is.False);
                GeneratedFile denied = await harness.GenerateAsync(harness.SecondContext);
                Assert.That(denied.Status.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            }
            finally
            {
                release.TrySetResult(true);
                await closing.WaitAsync(s_timeout);
            }
            harness.AssertRemoved(file);
            GeneratedFile next = await harness.GenerateAsync(harness.SecondContext);
            Assert.That(next.Status.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Read(next, harness.SecondContext), Is.EqualTo(ByteString.From(1, 2, 3)));
        }

        /// <summary>
        /// Owner cleanup preserves another session's active handle and refuses cross-session reads.
        /// </summary>
        [Test]
        public async Task ClosingRemovesOnlyItsOwnerAndLeavesTheOtherHandleReadable()
        {
            await using var harness = new Harness(capacity: 2);
            await harness.InitializeAsync();
            GeneratedFile first = await harness.GenerateAsync(harness.FirstContext);
            GeneratedFile second = await harness.GenerateAsync(harness.SecondContext);
            FileState file = harness.File(second);
            ByteString bytes = default;
            ServiceResult denied = file.Read!.OnCall!(
                harness.FirstContext, file.Read, file.NodeId, second.Handle, 3, ref bytes);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            await harness.CloseSessionAsync(false);

            harness.AssertRemoved(first);
            Assert.That(harness.Read(second, harness.SecondContext), Is.EqualTo(ByteString.From(1, 2, 3)));
            Assert.That(harness.Manager.RemovedFiles.ContainsKey(second.NodeId), Is.False);
            await Assert.ThatAsync(
                () => harness.GenerateAsync(harness.FirstContext),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSessionClosed));
        }

        /// <summary>
        /// Result-transfer handles cannot be generated without a session owner.
        /// </summary>
        [Test]
        public async Task SessionlessCallerCannotGenerateResultTransferHandle()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();

            GeneratedFile generated = await harness.GenerateAsync(harness.Manager.SystemContext);

            Assert.That(generated.Status.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(harness.TransientFiles(), Is.Empty);
            harness.Store.Verify(
                value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// A sessionless caller cannot read a result-transfer handle owned by a session.
        /// </summary>
        [Test]
        public async Task SessionlessCallerCannotReadAnotherSessionsResultFile()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            GeneratedFile generated = await harness.GenerateAsync(harness.FirstContext);
            FileState file = harness.File(generated);
            ByteString bytes = default;

            ServiceResult denied = file.Read!.OnCall!(
                harness.Manager.SystemContext,
                file.Read,
                file.NodeId,
                generated.Handle,
                3,
                ref bytes);

            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(bytes.IsNull, Is.True);
            Assert.That(harness.Read(generated, harness.FirstContext), Is.EqualTo(ByteString.From(1, 2, 3)));
        }

        /// <summary>
        /// Timeout sweeping reclaims only inactive handles and finishes before granting a replacement.
        /// </summary>
        [Test]
        public async Task ExpiryPreservesAnActiveOwnerAndReleasesTheExpiredReservation()
        {
            await using var harness = new Harness(capacity: 2);
            await harness.InitializeAsync();
            GeneratedFile first = await harness.GenerateAsync(harness.FirstContext);
            GeneratedFile second = await harness.GenerateAsync(harness.SecondContext);
            harness.Clock.Advance(TimeSpan.FromSeconds(4));
            Assert.That(harness.Read(second, harness.SecondContext), Is.EqualTo(ByteString.From(1, 2, 3)));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));

            GeneratedFile replacement = await harness.GenerateAsync(harness.SecondContext);

            harness.AssertRemoved(first);
            Assert.That(replacement.Status.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Manager.FindPredefinedNode<FileState>(second.NodeId), Is.Not.Null);
        }

        /// <summary>
        /// Late provider completion cannot reserve or publish a file after cancellation, expiry or shutdown.
        /// </summary>
        [TestCase("close")]
        [TestCase("expiry")]
        [TestCase("cancel")]
        [TestCase("dispose")]
        public async Task PendingProviderIsDrainedWithoutPublishingAfterTermination(string termination)
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            using var cancellation = new CancellationTokenSource();
            var entered = Signal();
            var release = new TaskCompletionSource<MachineryResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken providerToken = default;
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken token) =>
                {
                    providerToken = token;
                    entered.TrySetResult(true);
                    return new ValueTask<MachineryResult?>(release.Task);
                });
            Task<GeneratedFile> pending = harness.GenerateAsync(harness.FirstContext, cancellation.Token);
            Task? closing = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout);
                if (termination == "cancel")
                {
                    cancellation.Cancel();
                }
                else if (termination == "dispose")
                {
                    closing = harness.Runtime.DisposeAsync().AsTask();
                }
                else
                {
                    harness.FirstSession.SetupGet(value => value.HasExpired).Returns(termination == "expiry");
                    closing = harness.CloseSessionAsync(false);
                }
                Assert.That(providerToken.IsCancellationRequested, Is.True);
                if (closing != null)
                {
                    Assert.That(closing.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(s_result);
            }
            await Assert.ThatAsync(() => pending.WaitAsync(s_timeout),
                Throws.InstanceOf<OperationCanceledException>());
            if (closing != null)
            {
                await closing.WaitAsync(s_timeout);
            }
            Assert.That(harness.TransientFiles(), Is.Empty);
            Assert.That(harness.Manager.RemovedFiles, Is.Empty);
            harness.ReturnResult();
            GeneratedFile next = await harness.GenerateAsync(harness.SecondContext);
            Assert.That(next.Status.StatusCode,
                Is.EqualTo(termination == "dispose" ? StatusCodes.BadInvalidState : StatusCodes.Good));
        }

        /// <summary>
        /// Cooperating stores are canceled by session close without needing their result.
        /// </summary>
        [Test]
        public async Task ClosingCancelsACooperatingStoreAndFreesItsPendingReservation()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            var entered = Signal();
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken token) => new ValueTask<MachineryResult?>(ReadAsync(token)));
            Task<GeneratedFile> pending = harness.GenerateAsync(harness.FirstContext);
            await entered.Task.WaitAsync(s_timeout);

            await harness.CloseSessionAsync(false).WaitAsync(s_timeout);

            await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>());
            harness.ReturnResult();
            Assert.That((await harness.GenerateAsync(harness.SecondContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.Good));

            async Task<MachineryResult?> ReadAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return s_result;
            }
        }

        /// <summary>
        /// Partially indexed files are drained and rolled back before any termination operation completes.
        /// </summary>
        [TestCase("close")]
        [TestCase("cancel")]
        [TestCase("dispose")]
        public async Task PendingRegistrationCannotLeaveALateFile(string termination)
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            using var cancellation = new CancellationTokenSource();
            var entered = Signal();
            var release = Signal();
            NodeId staged = default;
            CancellationToken registrationToken = default;
            harness.Manager.RegisteredFile = async (file, token) =>
            {
                staged = file.NodeId;
                registrationToken = token;
                entered.TrySetResult(true);
                await release.Task;
            };
            Task<GeneratedFile> pending = harness.GenerateAsync(harness.FirstContext, cancellation.Token);
            Task? closing = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout);
                Assert.That(harness.Manager.FindPredefinedNode<FileState>(staged), Is.Not.Null);
                if (termination == "cancel")
                {
                    cancellation.Cancel();
                }
                else
                {
                    closing = termination == "dispose"
                        ? harness.Runtime.DisposeAsync().AsTask()
                        : harness.CloseSessionAsync(true);
                }
                Assert.That(registrationToken.IsCancellationRequested, Is.True);
                if (closing != null)
                {
                    Assert.That(closing.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Assert.ThatAsync(() => pending.WaitAsync(s_timeout),
                Throws.InstanceOf<OperationCanceledException>());
            if (closing != null)
            {
                await closing.WaitAsync(s_timeout);
            }
            Assert.That(harness.Manager.FindPredefinedNode<FileState>(staged), Is.Null);
            Assert.That(harness.TransientFiles(), Is.Empty);
            Assert.That(harness.Manager.RemovedFiles[staged], Is.EqualTo(1));
        }

        /// <summary>
        /// Canceled registration before indexing removes staging and never attaches nodes after the close boundary.
        /// </summary>
        [Test]
        public async Task CancellationBeforeIndexingRollsBackStaging()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            var entered = Signal();
            var release = Signal();
            harness.Manager.RegisteringFile = async (_, _) =>
            {
                entered.TrySetResult(true);
                await release.Task;
            };
            Task<GeneratedFile> pending = harness.GenerateAsync(harness.FirstContext);
            await entered.Task.WaitAsync(s_timeout);
            Task closing = harness.CloseSessionAsync(false);
            try
            {
                Assert.That(closing.IsCompleted, Is.False);
                Assert.That(harness.Manager.NodeCount, Is.EqualTo(harness.InitialNodeCount));
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Assert.ThatAsync(() => pending.WaitAsync(s_timeout), Throws.InstanceOf<OperationCanceledException>());
            await closing.WaitAsync(s_timeout);
            Assert.That(harness.TransientFiles(), Is.Empty);
            Assert.That(harness.Manager.RemovedFiles, Is.Empty);
        }

        /// <summary>
        /// The live session flag rejects a late provider result even before the awaited close hook is dispatched.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LateProviderRechecksTheOwnersTerminationFlag(bool expired)
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            var release = new TaskCompletionSource<MachineryResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineryResult?>(release.Task));
            Task<GeneratedFile> pending = harness.GenerateAsync(harness.FirstContext);
            harness.FirstSession.SetupGet(value => value.IsClosing).Returns(!expired);
            harness.FirstSession.SetupGet(value => value.HasExpired).Returns(expired);

            release.TrySetResult(s_result);
            GeneratedFile rejected = await pending.WaitAsync(s_timeout);

            Assert.That(rejected.Status.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(harness.TransientFiles(), Is.Empty);
            harness.ReturnResult();
            Assert.That((await harness.GenerateAsync(harness.SecondContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A termination flag set during registration is checked before publishing output and rolls back indexed nodes.
        /// </summary>
        [Test]
        public async Task RegistrationRechecksTheOwnerBeforePublishingItsHandle()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            harness.Manager.RegisteredFile = (_, _) =>
            {
                harness.FirstSession.SetupGet(value => value.IsClosing).Returns(true);
                return default;
            };

            GeneratedFile rejected = await harness.GenerateAsync(harness.FirstContext);

            Assert.That(rejected.Status.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(rejected.NodeId.IsNull, Is.True);
            Assert.That(rejected.Handle, Is.Zero);
            Assert.That(harness.Manager.NodeCount, Is.EqualTo(harness.InitialNodeCount));
            Assert.That(harness.TransientFiles(), Is.Empty);
        }

        /// <summary>
        /// Explicit file close, owner close, expiry and runtime shutdown share the same physical removal.
        /// </summary>
        [Test]
        public async Task ConcurrentCleanupPathsJoinOneDeletion()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            GeneratedFile generated = await harness.GenerateAsync(harness.FirstContext);
            FileState file = harness.File(generated);
            var entered = Signal();
            var release = Signal();
            harness.Manager.RemovingFile = async _ =>
            {
                entered.TrySetResult(true);
                await release.Task;
            };
            Task<CloseMethodStateResult> fileClose = file.Close!.OnCallAsync!(
                harness.FirstContext, file.Close, file.NodeId, generated.Handle, CancellationToken.None).AsTask();
            Task? sessionClose = null;
            Task? disposing = null;
            Task<GeneratedFile>? sweeping = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout);
                sessionClose = harness.CloseSessionAsync(false);
                harness.Clock.Advance(TimeSpan.FromSeconds(6));
                sweeping = harness.GenerateAsync(harness.SecondContext);
                disposing = harness.Runtime.DisposeAsync().AsTask();
                Assert.That(fileClose.IsCompleted, Is.False);
                Assert.That(sessionClose.IsCompleted, Is.False);
                Assert.That(disposing.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await fileClose.WaitAsync(s_timeout);
                if (sessionClose != null)
                {
                    await sessionClose.WaitAsync(s_timeout);
                }
                if (disposing != null)
                {
                    await disposing.WaitAsync(s_timeout);
                }
                if (sweeping != null)
                {
                    Assert.That((await sweeping.WaitAsync(s_timeout)).Status.StatusCode,
                        Is.EqualTo(StatusCodes.BadInvalidState));
                }
            }
            harness.AssertRemoved(generated);
            Assert.That(harness.Manager.RemovedFiles[generated.NodeId], Is.EqualTo(1));
        }

        /// <summary>
        /// A failed callback does not skip other files or descendants; failure remains observable and retryable.
        /// </summary>
        [Test]
        public async Task FailedDeletionAttemptsEveryFileAndShutdownRetriesRetainedOwnership()
        {
            await using var harness = new Harness(capacity: 2);
            await harness.InitializeAsync();
            GeneratedFile first = await harness.GenerateAsync(harness.FirstContext);
            GeneratedFile second = await harness.GenerateAsync(harness.FirstContext);
            harness.Manager.RemovingFile = _ => throw new InvalidOperationException("removal callback");

            Task closing = harness.CloseSessionAsync(false);
            await Assert.ThatAsync(async () => await closing, Throws.TypeOf<AggregateException>());

            Assert.That(harness.Manager.RemovedFiles[first.NodeId], Is.EqualTo(1));
            Assert.That(harness.Manager.RemovedFiles[second.NodeId], Is.EqualTo(1));
            Assert.That(harness.Manager.NodeCount, Is.EqualTo(harness.InitialNodeCount));
            Assert.That((await harness.GenerateAsync(harness.SecondContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
            harness.Manager.RemovingFile = null;
            await harness.Runtime.DisposeAsync();
            Assert.That(harness.TransientFiles(), Is.Empty);
        }

        /// <summary>
        /// Provider failure, absent payload and partial registration failure all release the reserved capacity.
        /// </summary>
        [Test]
        public async Task GenerationFailuresRollBackBeforeAnotherCallCanUseCapacity()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("provider"));
            await Assert.ThatAsync(() => harness.GenerateAsync(harness.FirstContext),
                Throws.TypeOf<InvalidOperationException>());
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .ReturnsAsync((MachineryResult?)null);
            Assert.That((await harness.GenerateAsync(harness.FirstContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.BadNotFound));
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MachineryResult(
                    new ResultDataType { ResultMetaData = new ResultMetaDataType { ResultId = "result" } }));
            Assert.That((await harness.GenerateAsync(harness.FirstContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.BadNothingToDo));
            harness.ReturnResult();
            harness.Manager.RegisteredFile = (_, _) => throw new InvalidOperationException("registration");
            Assert.That((await harness.GenerateAsync(harness.FirstContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(harness.TransientFiles(), Is.Empty);
            harness.Manager.RegisteredFile = null;
            Assert.That((await harness.GenerateAsync(harness.FirstContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// A canceled request cannot invoke the provider or reserve a handle.
        /// </summary>
        [Test]
        public async Task PreCanceledGenerationDoesNotInvokeTheProvider()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(() => harness.GenerateAsync(harness.FirstContext, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            harness.Store.Verify(value => value.GetResultByIdAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That((await harness.GenerateAsync(harness.SecondContext)).Status.StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Manager shutdown awaits its owned transfer runtime, including an in-progress store read.
        /// </summary>
        [Test]
        public async Task ManagerShutdownDrainsOwnedPendingGeneration()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            var entered = Signal();
            var release = new TaskCompletionSource<MachineryResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<MachineryResult?>(release.Task);
                });
            Task<GeneratedFile> pending = harness.GenerateAsync(harness.FirstContext);
            await entered.Task.WaitAsync(s_timeout);
            Task disposing = harness.Manager.DisposeAsync().AsTask();
            try
            {
                Assert.That(disposing.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(s_result);
            }
            await Assert.ThatAsync(() => pending.WaitAsync(s_timeout),
                Throws.InstanceOf<OperationCanceledException>());
            await disposing.WaitAsync(s_timeout);
            Assert.That(harness.TransientFiles(), Is.Empty);
        }

        /// <summary>
        /// Manager shutdown and an already admitted owner cleanup must not deadlock or lose descendants.
        /// </summary>
        [Test]
        public async Task ManagerShutdownJoinsAnAdmittedSessionCleanup()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            GeneratedFile generated = await harness.GenerateAsync(harness.FirstContext);
            var entered = Signal();
            var release = Signal();
            harness.Manager.RemovingFile = async _ =>
            {
                entered.TrySetResult(true);
                await release.Task;
            };
            Task closing = harness.CloseSessionAsync(false);
            await entered.Task.WaitAsync(s_timeout);
            Task disposing = harness.Manager.DisposeAsync().AsTask();
            try
            {
                Assert.That(disposing.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
            await Task.WhenAll(closing, disposing).WaitAsync(s_timeout);
            Assert.That(harness.Manager.RemovedFiles[generated.NodeId], Is.EqualTo(1));
            Assert.That(harness.TransientFiles(), Is.Empty);
        }

        /// <summary>
        /// Concurrent asynchronous disposal callers observe the same cleanup failure rather than early success.
        /// </summary>
        [Test]
        public async Task DisposalFailuresRemainObservableToEveryCaller()
        {
            await using var harness = new Harness(capacity: 1);
            await harness.InitializeAsync();
            await harness.GenerateAsync(harness.FirstContext);
            harness.Manager.RemovingFile = _ => throw new InvalidOperationException("cleanup");
            Task first = harness.Runtime.DisposeAsync().AsTask();
            Task second = harness.Runtime.DisposeAsync().AsTask();
            try
            {
                await Assert.ThatAsync(async () => await first, Throws.TypeOf<AggregateException>());
                await Assert.ThatAsync(async () => await second, Throws.TypeOf<AggregateException>());
                Assert.That(first.Exception!.Flatten().InnerExceptions[0].Message, Is.EqualTo("cleanup"));
                Assert.That(second.Exception!.Flatten().InnerExceptions[0].Message, Is.EqualTo("cleanup"));
                await harness.Manager.SessionClosingAsync(null!, harness.FirstSession.Object.Id, false);
            }
            finally
            {
                // The deliberate failure was observed above; the manager has no live file nodes left.
                harness.Manager.OwnedResource = null;
            }
        }

        /// <summary>
        /// Creates a barrier whose continuations never run inline inside cleanup.
        /// </summary>
        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Captures a generation's public output.
        /// </summary>
        private readonly record struct GeneratedFile(ServiceResult Status, NodeId NodeId, uint Handle);

        /// <summary>
        /// Owns the real transfer implementation over a deterministic node manager and session registry.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            /// <summary>
            /// Builds one transfer resource and two distinct live session contexts.
            /// </summary>
            public Harness(int capacity)
            {
                var server = new Mock<IServerInternal>();
                var sessions = new Mock<ISessionManager>();
                var master = new Mock<IMasterNodeManager>();
                var namespaces = new NamespaceTable();
                namespaces.Append(Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_queues = new MonitoredItemQueueFactory(telemetry);
                server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(m_queues);
                server.SetupGet(value => value.NodeManager).Returns(master.Object);
                master.SetupGet(value => value.ConfigurationNodeManager).Returns(Mock.Of<IConfigurationNodeManager>());
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
                FirstSession.SetupGet(value => value.Id).Returns(new NodeId("first", 1));
                var second = new Mock<ISession>();
                second.SetupGet(value => value.Id).Returns(new NodeId("second", 1));
                sessions.Setup(value => value.GetSessions()).Returns(() => [FirstSession.Object, second.Object]);
                Manager = new TestNodeManager(server.Object, telemetry.CreateLogger<TestNodeManager>());
                FirstContext = Manager.SystemContext.Copy(FirstSession.Object);
                SecondContext = Manager.SystemContext.Copy(second.Object);
                Transfer = Manager.SystemContext.CreateInstanceOfResultTransferType(
                    new BaseObjectState(null), new QualifiedName("Transfer", Manager.NamespaceIndexes[0]));
                Transfer.NodeId = new NodeId("transfer", Manager.NamespaceIndexes[0]);
                ReturnResult();
                Runtime = new MachineryResultTransferManager(
                    Transfer, Manager, Store.Object,
                    new MachineryServerOptions
                    {
                        MaxConcurrentResultTransfers = capacity,
                        ResultTransferTimeout = TimeSpan.FromSeconds(5)
                    },
                    telemetry.CreateLogger<MachineryResultTransferManager>(), Clock);
                Manager.OwnedResource = Runtime;
            }

            /// <summary>
            /// Gets the namespace owner with registration and removal barriers.
            /// </summary>
            public TestNodeManager Manager { get; }

            /// <summary>
            /// Gets the resource under test.
            /// </summary>
            public MachineryResultTransferManager Runtime { get; }

            /// <summary>
            /// Gets the generated transfer object.
            /// </summary>
            public ResultTransferState Transfer { get; }

            /// <summary>
            /// Gets the caller whose session will be closed.
            /// </summary>
            public Mock<ISession> FirstSession { get; } = new();

            /// <summary>
            /// Gets the first caller context.
            /// </summary>
            public ServerSystemContext FirstContext { get; }

            /// <summary>
            /// Gets the independent surviving caller context.
            /// </summary>
            public ServerSystemContext SecondContext { get; }

            /// <summary>
            /// Gets the controllable payload provider.
            /// </summary>
            public Mock<IMachineryResultStore> Store { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets the deterministic inactivity clock.
            /// </summary>
            public ManualTimeProvider Clock { get; } = new();

            /// <summary>
            /// Gets the number of nodes before any transfer.
            /// </summary>
            public int InitialNodeCount { get; private set; }

            /// <summary>
            /// Registers the generated transfer object.
            /// </summary>
            public async Task InitializeAsync()
            {
                await Manager.AddPredefinedNodeAsync(Transfer);
                InitialNodeCount = Manager.NodeCount;
            }

            /// <summary>
            /// Restores the immutable payload provider.
            /// </summary>
            public void ReturnResult()
            {
                Store.Setup(value => value.GetResultByIdAsync("result", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(s_result);
            }

            /// <summary>
            /// Exercises the bound method callback without replacing the transfer implementation.
            /// </summary>
            public async Task<GeneratedFile> GenerateAsync(
                ServerSystemContext context,
                CancellationToken cancellationToken = default)
            {
                var outputs = new List<Variant> { Variant.Null, Variant.Null, Variant.Null };
                ServiceResult status = await Transfer.GenerateFileForRead!.OnCallMethod2Async!(
                    context, Transfer.GenerateFileForRead, Transfer.NodeId,
                    [Variant.FromStructure(new ResultTransferOptionsDataType { ResultId = "result" })],
                    outputs, cancellationToken);
                outputs[0].TryGetValue(out NodeId fileId);
                outputs[1].TryGetValue(out uint handle);
                return new GeneratedFile(status, fileId, handle);
            }

            /// <summary>
            /// Resolves the actual generated FileType in the address space.
            /// </summary>
            public FileState File(GeneratedFile generated)
            {
                Assert.That(generated.Status.StatusCode, Is.EqualTo(StatusCodes.Good));
                return Manager.FindPredefinedNode<FileState>(generated.NodeId)!;
            }

            /// <summary>
            /// Reads through the bound FileType method.
            /// </summary>
            public ByteString Read(GeneratedFile generated, ServerSystemContext context)
            {
                FileState file = File(generated);
                ByteString bytes = default;
                ServiceResult result = file.Read!.OnCall!(
                    context, file.Read, file.NodeId, generated.Handle, 3, ref bytes);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                return bytes;
            }

            /// <summary>
            /// Marks the session closing as ServerInternalData does before dispatching awaited callbacks.
            /// </summary>
            public Task CloseSessionAsync(bool deleteSubscriptions)
            {
                FirstSession.SetupGet(value => value.IsClosing).Returns(true);
                return Manager.SessionClosingAsync(null!, FirstSession.Object.Id, deleteSubscriptions).AsTask();
            }

            /// <summary>
            /// Finds retained transient child files, including unindexed staging.
            /// </summary>
            public List<BaseInstanceState> TransientFiles()
            {
                var children = new List<BaseInstanceState>();
                Transfer.GetChildren(Manager.SystemContext, children);
                return children.FindAll(static child => child is FileState);
            }

            /// <summary>
            /// Checks both the index and the transfer object's child links.
            /// </summary>
            public void AssertRemoved(GeneratedFile generated)
            {
                Assert.That(Manager.FindPredefinedNode<FileState>(generated.NodeId), Is.Null);
                Assert.That(TransientFiles().Exists(file => file.NodeId == generated.NodeId), Is.False);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync();
                m_queues.Dispose();
            }

            /// <summary>Queue resources owned by this in-memory server.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }

        /// <summary>
        /// Makes provider-independent registration and removal races deterministic.
        /// </summary>
        private sealed class TestNodeManager(IServerInternal server, ILogger logger)
            : AsyncCustomNodeManager(server, logger, "urn:tests:result-transfer")
        {
            /// <summary>
            /// Gets or sets a barrier before registration starts.
            /// </summary>
            public Func<FileState, CancellationToken, ValueTask>? RegisteringFile { get; set; }

            /// <summary>
            /// Gets or sets a barrier after a file and its descendants have been indexed.
            /// </summary>
            public Func<FileState, CancellationToken, ValueTask>? RegisteredFile { get; set; }

            /// <summary>
            /// Gets or sets a removal callback before descendant deletion.
            /// </summary>
            public Func<FileState, ValueTask>? RemovingFile { get; set; }

            /// <summary>
            /// Gets the number of physical removals of each transient file.
            /// </summary>
            public ConcurrentDictionary<NodeId, int> RemovedFiles { get; } = new();

            /// <summary>
            /// Gets or sets the resource owned by the manager's asynchronous disposal.
            /// </summary>
            public MachineryResultTransferManager? OwnedResource { get; set; }

            /// <summary>
            /// Gets the number of indexed nodes.
            /// </summary>
            public int NodeCount => PredefinedNodes.Count;

            /// <inheritdoc/>
            protected override async ValueTask AddPredefinedNodeAsync(
                ISystemContext context, NodeState node, CancellationToken cancellationToken = default)
            {
                if (node is FileState staged && RegisteringFile != null)
                {
                    await RegisteringFile(staged, cancellationToken);
                }
                await base.AddPredefinedNodeAsync(context, node, cancellationToken);
                if (node is FileState file && RegisteredFile != null)
                {
                    await RegisteredFile(file, cancellationToken);
                }
            }

            /// <inheritdoc/>
            protected override async ValueTask OnNodeRemovedAsync(
                NodeState node, CancellationToken cancellationToken = default)
            {
                if (node is FileState file)
                {
                    RemovedFiles.AddOrUpdate(file.NodeId, 1, static (_, count) => count + 1);
                    if (RemovingFile != null)
                    {
                        await RemovingFile(file);
                    }
                }
                await base.OnNodeRemovedAsync(node, cancellationToken);
            }

            /// <inheritdoc/>
            protected override async ValueTask DisposeAsyncCore()
            {
                if (OwnedResource != null)
                {
                    await OwnedResource.DisposeAsync();
                }
                await base.DisposeAsyncCore();
            }
        }

        /// <summary>
        /// Advances expiry without sleeps or wall-clock dependence.
        /// </summary>
        private sealed class ManualTimeProvider : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow() => m_now;

            /// <summary>
            /// Advances the transfer inactivity clock.
            /// </summary>
            public void Advance(TimeSpan elapsed)
            {
                m_now += elapsed;
            }

            /// <summary>The deterministic clock value.</summary>
            private DateTimeOffset m_now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        /// <summary>The bounded wait for test-owned barriers.</summary>
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

        /// <summary>The immutable payload shared by independent test stores.</summary>
        private static readonly MachineryResult s_result = new(
            new ResultDataType { ResultMetaData = new ResultMetaDataType { ResultId = "result" } },
            ByteString.From(1, 2, 3));
    }
}
