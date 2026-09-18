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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Verifies directory capacity admission, refresh publication, and cleanup across concurrent binding operations.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileDirectoryLifecycleRegressionTests
    {
        /// <summary>
        /// Verifies that create, copy, and move operations check destination capacity before mutating the provider.
        /// </summary>
        [TestCase("file", 2)]
        [TestCase("directory", 2)]
        [TestCase("copy", 2)]
        [TestCase("move", 2)]
        [TestCase("file", 3)]
        [TestCase("directory", 3)]
        [TestCase("copy", 3)]
        [TestCase("move", 3)]
        public async Task CapacityAdmissionPrecedesProviderMutationAsync(string operation, int maximum)
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateDirectoryAsync("target", CancellationToken.None).ConfigureAwait(false);
            await harness.Physical.CreateFileAsync("source", CancellationToken.None).ConfigureAwait(false);
            await harness.Physical.CreateFileAsync("target/a", CancellationToken.None).ConfigureAwait(false);
            await harness.Physical.CreateFileAsync("target/b", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(maximum).ConfigureAwait(false);
            var host = (IFileSystemHost)binding;
            FileDirectoryState target = harness.FindDirectory("target");
            MethodState method = operation switch
            {
                "file" => target.CreateFile!,
                "directory" => target.CreateDirectory!,
                _ => harness.Root.MoveOrCopy!
            };
            ArrayOf<Variant> arguments = operation switch
            {
                "file" => ["new", false],
                "directory" => ["new"],
                _ => [host.BuildFileNodeId("source"), target.NodeId, operation == "copy", "new"]
            };
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                method, harness.Context, method.Parent!.NodeId, arguments).ConfigureAwait(false);
            bool admitted = maximum == 3;
            Assert.That(result.StatusCode, Is.EqualTo(admitted ? StatusCodes.Good : StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.MutationCount, Is.EqualTo(admitted ? 1 : 0));
            FileSystemEntry? created = await harness.Physical.GetEntryAsync("target/new", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(created.HasValue, Is.EqualTo(admitted));
            FileSystemEntry? source = await harness.Physical.GetEntryAsync("source", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(source.HasValue, Is.EqualTo(!admitted || operation != "move"));
        }

        /// <summary>
        /// Verifies that renaming within a full directory succeeds without reserving an additional entry.
        /// </summary>
        [Test]
        public async Task RenameWithinFullDirectoryDoesNotConsumeAnotherEntryAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("source", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(1).ConfigureAwait(false);
            var host = (IFileSystemHost)binding;
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.MoveOrCopy, harness.Context, harness.Root.NodeId,
                [host.BuildFileNodeId("source"), harness.Root.NodeId, false, "renamed"]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await harness.Physical.GetEntryAsync("source", CancellationToken.None).ConfigureAwait(false),
                Is.Null);
            Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("renamed"), out string path, out _, out _), Is.True);
            Assert.That(path, Is.EqualTo("renamed"));
        }

        /// <summary>
        /// Verifies that post-commit refresh failures are logged without reporting a successful mutation as failed.
        /// </summary>
        [TestCase("io")]
        [TestCase("limit")]
        [TestCase("cancellation")]
        public async Task CommittedMutationIsNotReportedAsProviderFailureWhenRefreshFailsAsync(string failure)
        {
            using var harness = new BindingHarness();
            await using IFileDirectoryBinding binding = await harness.BindAsync(2).ConfigureAwait(false);
            harness.RefreshFailure = failure switch
            {
                "io" => new IOException("refresh unavailable"),
                "limit" => new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded),
                _ => new OperationCanceledException()
            };
            (ServiceResult result, List<Variant> output) = await FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["created", false]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await harness.Physical.GetEntryAsync("created", CancellationToken.None).ConfigureAwait(false),
                Is.Not.Null);
            Assert.That(output[0].TryGetValue(out NodeId returnedId), Is.True);
            harness.Logger.Verify(logger => logger.Log(
                LogLevel.Error,
                It.Is<EventId>(id => id.Name == "FileDirectoryRefreshFailed"),
                It.IsAny<It.IsAnyType>(),
                harness.RefreshFailure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            harness.RefreshFailure = null;
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(((IFileSystemHost)binding).TryGetProviderPath(returnedId, out string path, out _, out _), Is.True);
            Assert.That(path, Is.EqualTo("created"));
        }

        /// <summary>
        /// Verifies that a corrective deletion can restore capacity and publish a fresh lookup after repeated failures.
        /// </summary>
        [Test]
        public async Task CorrectiveDeleteRecoversAfterPersistentOverLimitRefreshFailureAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("keep", CancellationToken.None).ConfigureAwait(false);
            await harness.Physical.CreateFileAsync("delete-me", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(2).ConfigureAwait(false);
            var host = (IFileSystemHost)binding;
            NodeId originalId = harness.FindFile("keep").NodeId;
            NodeId deletedId = harness.FindFile("delete-me").NodeId;
            await harness.Physical.CreateFileAsync("external", CancellationToken.None).ConfigureAwait(false);

            (ServiceResult renamed, List<Variant> output) = await FileReadRegressionTests.CallAsync(
                harness.Root.MoveOrCopy, harness.Context, harness.Root.NodeId,
                [originalId, harness.Root.NodeId, false, "renamed"]).ConfigureAwait(false);

            Assert.That(renamed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(output[0].TryGetValue(out NodeId renamedId), Is.True);
            Assert.That(harness.MutationCount, Is.EqualTo(1));
            Assert.That(await harness.Physical.GetEntryAsync("keep", CancellationToken.None).ConfigureAwait(false),
                Is.Null);
            Assert.That(await harness.Physical.GetEntryAsync("renamed", CancellationToken.None).ConfigureAwait(false),
                Is.Not.Null);
            Assert.That(host.TryGetProviderPath(renamedId, out _, out _, out _), Is.False);
            harness.VerifyRefreshLimitFailures(1);
            harness.VerifyRefreshCommitState(true, 1);
            harness.VerifyRefreshCommitState(false, 0);

            ServiceResultException refreshFailure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await binding.RefreshAsync().ConfigureAwait(false))!;
            Assert.That(refreshFailure.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(host.TryGetProviderPath(originalId, out _, out _, out _), Is.True);
            Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("external"), out _, out _, out _), Is.False);

            (ServiceResult deleted, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.DeleteFileSystemObject, harness.Context, harness.Root.NodeId, [deletedId])
                .ConfigureAwait(false);

            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.Good),
                "A failed pre-refresh must not block the delete that restores the binding limit.");
            Assert.That(harness.MutationCount, Is.EqualTo(2));
            harness.Provider.Verify(provider => provider.DeleteAsync("delete-me", It.IsAny<CancellationToken>()),
                Times.Once);
            var paths = new List<string>();
            await foreach (FileSystemEntry entry in harness.Physical.EnumerateAsync(
                string.Empty, CancellationToken.None).ConfigureAwait(false))
            {
                paths.Add(entry.Path);
            }
            Assert.That(paths, Is.EquivalentTo(s_recoveredPaths));
            var children = new List<BaseInstanceState>();
            harness.Root.GetChildren(harness.Context, children);
            Assert.That(children.OfType<FileState>().Select(file => file.BrowseName.Name),
                Is.EquivalentTo(s_recoveredPaths));
            Assert.That(host.TryGetProviderPath(originalId, out _, out _, out _), Is.False);
            Assert.That(host.TryGetProviderPath(deletedId, out _, out _, out _), Is.False);
            Assert.That(host.TryGetProviderPath(renamedId, out string path, out _, out _), Is.True);
            Assert.That(path, Is.EqualTo("renamed"));
            harness.VerifyRefreshLimitFailures(2);
            harness.VerifyRefreshCommitState(true, 1);
            harness.VerifyRefreshCommitState(false, 1);

            await binding.RefreshAsync().ConfigureAwait(false);
            harness.VerifyRefreshLimitFailures(2);
            Assert.That(harness.MutationCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that repeated refresh failures do not permit a create operation to exceed directory capacity.
        /// </summary>
        [Test]
        public async Task PersistentRefreshFailureDoesNotBypassCapacityAdmissionAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("keep", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(1).ConfigureAwait(false);
            NodeId originalId = harness.FindFile("keep").NodeId;
            await harness.Physical.CreateFileAsync("external", CancellationToken.None).ConfigureAwait(false);
            (ServiceResult renamed, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.MoveOrCopy, harness.Context, harness.Root.NodeId,
                [originalId, harness.Root.NodeId, false, "renamed"]).ConfigureAwait(false);
            Assert.That(renamed.StatusCode, Is.EqualTo(StatusCodes.Good));

            (ServiceResult created, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["blocked", false])
                .ConfigureAwait(false);

            Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.MutationCount, Is.EqualTo(1));
            harness.Provider.Verify(provider => provider.CreateFileAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await harness.Physical.GetEntryAsync("blocked", CancellationToken.None).ConfigureAwait(false),
                Is.Null);
            harness.VerifyRefreshLimitFailures(2);
            harness.VerifyRefreshCommitState(true, 1);
            harness.VerifyRefreshCommitState(false, 1);
        }

        /// <summary>
        /// Verifies that cancellation thrown by the pre-mutation refresh prevents provider deletion.
        /// </summary>
        [Test]
        public async Task PreMutationRefreshCancellationDoesNotReachProviderAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("keep", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(2).ConfigureAwait(false);
            NodeId keptId = harness.FindFile("keep").NodeId;
            harness.RefreshFailure = new IOException("post-refresh unavailable");
            (ServiceResult created, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["created", false])
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
            harness.RefreshFailure = new OperationCanceledException("pre-refresh cancelled");

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await harness.Root.DeleteFileSystemObject!.OnCallAsync!(
                    harness.Context, harness.Root.DeleteFileSystemObject, harness.Root.NodeId, keptId,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(harness.MutationCount, Is.EqualTo(1));
            harness.Provider.Verify(provider => provider.DeleteAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await harness.Physical.GetEntryAsync("keep", CancellationToken.None).ConfigureAwait(false),
                Is.Not.Null);
            harness.VerifyRefreshCommitState(true, 1);
            harness.VerifyRefreshCommitState(false, 0);
        }

        /// <summary>
        /// Verifies that request cancellation during a failing pre-refresh is propagated before provider mutation.
        /// </summary>
        [Test]
        public async Task CancellationDuringFailedPreRefreshDoesNotReachProviderAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("keep", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(2).ConfigureAwait(false);
            NodeId keptId = harness.FindFile("keep").NodeId;
            harness.RefreshFailure = new IOException("refresh unavailable");
            (ServiceResult created, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["created", false])
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
            using var cancellation = new CancellationTokenSource();
            harness.BeforeEnumeration = cancellation.Cancel;

            OperationCanceledException failure = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await harness.Root.DeleteFileSystemObject!.OnCallAsync!(
                    harness.Context, harness.Root.DeleteFileSystemObject, harness.Root.NodeId, keptId,
                    cancellation.Token).ConfigureAwait(false))!;

            Assert.That(failure.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(harness.MutationCount, Is.EqualTo(1));
            harness.Provider.Verify(provider => provider.DeleteAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await harness.Physical.GetEntryAsync("keep", CancellationToken.None).ConfigureAwait(false),
                Is.Not.Null);
            harness.VerifyRefreshCommitState(true, 1);
            harness.VerifyRefreshCommitState(false, 1);
        }

        /// <summary>
        /// Verifies that recoverable pre-refresh failures do not replace the error returned by a corrective deletion.
        /// </summary>
        [TestCase("io")]
        [TestCase("access")]
        [TestCase("unsupported")]
        [TestCase("limit")]
        [TestCase("registration")]
        [TestCase("missing-directory")]
        public async Task PreMutationRefreshFailurePreservesProviderDeleteFailureAsync(string failure)
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("keep", CancellationToken.None).ConfigureAwait(false);
            await using IFileDirectoryBinding binding = await harness.BindAsync(2).ConfigureAwait(false);
            NodeId keptId = harness.FindFile("keep").NodeId;
            harness.RefreshFailure = failure switch
            {
                "io" => new IOException("refresh unavailable"),
                "access" => new UnauthorizedAccessException("enumeration denied"),
                "unsupported" => new NotSupportedException("enumeration unsupported"),
                "limit" => new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded),
                "registration" => new InvalidOperationException("registration unavailable"),
                "missing-directory" => new DirectoryNotFoundException("refresh directory unavailable"),
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            (ServiceResult created, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["created", false])
                .ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
            harness.Provider.Setup(provider => provider.DeleteAsync("keep", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException("delete denied"));

            (ServiceResult deleted, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.DeleteFileSystemObject, harness.Context, harness.Root.NodeId, [keptId])
                .ConfigureAwait(false);

            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(harness.MutationCount, Is.EqualTo(1));
            harness.Provider.Verify(provider => provider.DeleteAsync("keep", It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(await harness.Physical.GetEntryAsync("keep", CancellationToken.None).ConfigureAwait(false),
                Is.Not.Null);
            harness.VerifyRefreshCommitState(true, 1);
            harness.VerifyRefreshCommitState(false, 1);
        }

        /// <summary>
        /// Verifies that refresh retains the old lookup until registration of replacement nodes completes.
        /// </summary>
        [Test]
        public async Task RefreshPublishesLookupOnlyAfterRegistrationCompletesAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("old", CancellationToken.None).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IFileDirectoryBinding binding = await harness.BindAsync(2, async (node, ct) =>
            {
                if (node.BrowseName.Name == "new")
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            var host = (IFileSystemHost)binding;
            await harness.Physical.CreateFileAsync("new", CancellationToken.None).ConfigureAwait(false);
            await harness.Physical.DeleteAsync("old", CancellationToken.None).ConfigureAwait(false);
            Task refresh = binding.RefreshAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("old"), out _, out _, out _), Is.True);
                Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("new"), out _, out _, out _), Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await refresh.ConfigureAwait(false);
            }
            Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("old"), out _, out _, out _), Is.False);
            Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("new"), out string path, out _, out _), Is.True);
            Assert.That(path, Is.EqualTo("new"));
        }

        /// <summary>
        /// Verifies that disposal retires path lookup and handle admission before invoking provider stream cleanup.
        /// </summary>
        [Test]
        public async Task DisposalRetiresLookupAndHandlesBeforeCallingProviderCleanupAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("first", CancellationToken.None).ConfigureAwait(false);
            await harness.Physical.CreateFileAsync("second", CancellationToken.None).ConfigureAwait(false);
            IFileDirectoryBinding binding = await harness.BindAsync(2).ConfigureAwait(false);
            var host = (IFileSystemHost)binding;
            bool lookupWasRetired = false;
            bool handleWasRejected = false;
            using var stream = new CleanupStream(() =>
            {
                lookupWasRetired = !host.TryGetProviderPath(host.BuildFileNodeId("first"), out _, out _, out _);
                handleWasRejected = host.GetOrCreateHandle(host.BuildFileNodeId("second"), "second") == null;
            });
            harness.Provider.Setup(value => value.OpenReadAsync("first", It.IsAny<CancellationToken>()))
                .ReturnsAsync(stream);
            await FileReadRegressionTests.OpenAsync(harness.FindFile("first"), harness.Context).ConfigureAwait(false);
            await binding.DisposeAsync().ConfigureAwait(false);
            Assert.That(lookupWasRetired, Is.True);
            Assert.That(handleWasRejected, Is.True);
            Assert.That(stream.CanRead, Is.False);
            Assert.That(host.TryGetProviderPath(harness.Root.NodeId, out _, out _, out _), Is.False);
            await binding.DisposeAsync().ConfigureAwait(false);
            await binding.RefreshAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that concurrent create requests serialize capacity admission and only one uses the final slot.
        /// </summary>
        [Test]
        public async Task ConcurrentCreatesCannotBothConsumeTheLastCapacitySlotAsync()
        {
            using var harness = new BindingHarness();
            await using IFileDirectoryBinding binding = await harness.BindAsync(1).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Provider.Setup(value => value.CreateFileAsync("first", It.IsAny<CancellationToken>()))
                .Returns(async (string path, CancellationToken ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    await harness.CreateFileAsync(path, ct).ConfigureAwait(false);
                });
            Task<(ServiceResult Result, List<Variant> Output)> first = FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["first", false]).AsTask();
            Task<(ServiceResult Result, List<Variant> Output)>? second = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                second = FileReadRegressionTests.CallAsync(
                    harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["second", false]).AsTask();
                Assert.That(second.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await first.ConfigureAwait(false);
                if (second != null)
                {
                    await second.ConfigureAwait(false);
                }
            }
            Assert.That((await first.ConfigureAwait(false)).Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await second!.ConfigureAwait(false)).Result.StatusCode,
                Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.MutationCount, Is.EqualTo(1));
            Assert.That(await harness.Physical.GetEntryAsync("second", CancellationToken.None).ConfigureAwait(false),
                Is.Null);
        }

        /// <summary>
        /// Verifies that failed node registration is retried before a committed provider entry becomes discoverable.
        /// </summary>
        [Test]
        public async Task FailedRegistrationIsRetriedBeforeLookupIsPublishedAsync()
        {
            using var harness = new BindingHarness();
            int attempts = 0;
            await using IFileDirectoryBinding binding = await harness.BindAsync(2, (_, _) =>
            {
                if (++attempts == 1)
                {
                    throw new IOException("registration unavailable");
                }
                return default;
            }).ConfigureAwait(false);
            (ServiceResult result, _) = await FileReadRegressionTests.CallAsync(
                harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["created", false]).ConfigureAwait(false);
            var host = (IFileSystemHost)binding;
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("created"), out _, out _, out _), Is.False);
            Assert.That(attempts, Is.EqualTo(1));
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(host.TryGetProviderPath(host.BuildFileNodeId("created"), out _, out _, out _), Is.True);
        }

        /// <summary>
        /// Verifies that a cancelled waiter leaves the active refresh protected and queued disposal rejects late
        /// mutation.
        /// </summary>
        [Test]
        public async Task CancelledWaiterAndQueuedDisposalDrainWithoutReleasingAnotherOperationsGateAsync()
        {
            using var harness = new BindingHarness();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IFileDirectoryBinding binding = await harness.BindAsync(3, async (_, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
            await harness.Physical.CreateFileAsync("new", CancellationToken.None).ConfigureAwait(false);
            Task refresh = binding.RefreshAsync().AsTask();
            Task? disposing = null;
            Task<(ServiceResult Result, List<Variant> Output)>? mutation = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                Task waiting = binding.RefreshAsync(cancellation.Token).AsTask();
                cancellation.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting.ConfigureAwait(false));
                Assert.That(refresh.IsCompleted, Is.False);
                disposing = binding.DisposeAsync().AsTask();
                mutation = FileReadRegressionTests.CallAsync(
                    harness.Root.CreateFile, harness.Context, harness.Root.NodeId, ["late", false]).AsTask();
                Assert.That(disposing.IsCompleted, Is.False);
                Assert.That(mutation.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await refresh.ConfigureAwait(false);
                if (disposing != null)
                {
                    await disposing.ConfigureAwait(false);
                }
                if (mutation != null)
                {
                    await mutation.ConfigureAwait(false);
                }
                await binding.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That((await mutation!.ConfigureAwait(false)).Result.StatusCode, Is.EqualTo(StatusCodes.BadShutdown));
            Assert.That(harness.MutationCount, Is.Zero);
        }

        /// <summary>
        /// Defines the entries expected after corrective deletion restores an over-capacity directory.
        /// </summary>
        private static readonly string[] s_recoveredPaths = ["renamed", "external"];

        /// <summary>
        /// Supplies an isolated physical directory with observable mutations and injectable refresh failures.
        /// </summary>
        private sealed class BindingHarness : IDisposable
        {
            /// <summary>
            /// Creates a writable provider, session context, root node, and logger for binding lifecycle tests.
            /// </summary>
            public BindingHarness()
            {
                m_path = Path.Combine(Path.GetTempPath(), "FileDirectoryRegression-" + Guid.NewGuid().ToString("N"));
                Physical = new PhysicalFileSystemProvider(m_path, "regression");
                Provider.SetupGet(value => value.MountName).Returns("regression");
                Provider.SetupGet(value => value.IsWritable).Returns(true);
                Provider.Setup(value => value.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string path, CancellationToken ct) => EnumerateAsync(path, ct));
                Provider.Setup(value => value.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string path, CancellationToken ct) => Physical.GetEntryAsync(path, ct));
                Provider.Setup(value => value.CreateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string path, CancellationToken ct) => CreateFileAsync(path, ct));
                Provider.Setup(value => value.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string path, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_mutationCount);
                        return Physical.CreateDirectoryAsync(path, ct);
                    });
                Provider.Setup(value => value.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string path, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_mutationCount);
                        return Physical.DeleteAsync(path, ct);
                    });
                Provider.Setup(value => value.MoveAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string source, string target, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_mutationCount);
                        return Physical.MoveAsync(source, target, ct);
                    });
                Provider.Setup(value => value.CopyAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string source, string target, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_mutationCount);
                        return Physical.CopyAsync(source, target, ct);
                    });
                Logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
                var factory = new Mock<ILoggerFactory>();
                factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(Logger.Object);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
                var namespaces = new NamespaceTable();
                namespaces.Append("urn:file-directory-regression");
                Context = new SessionSystemContext(telemetry.Object)
                {
                    NamespaceUris = namespaces,
                    ServerUris = new StringTable(),
                    TypeTable = new TypeTable(namespaces),
                    SessionId = new NodeId("session", 1)
                };
                Root = new FileDirectoryState(null)
                {
                    TypeDefinitionId = ObjectTypeIds.FileDirectoryType,
                    NodeId = new NodeId("root", 1),
                    BrowseName = new QualifiedName("root", 1),
                    DisplayName = new LocalizedText("root")
                };
            }

            /// <summary>
            /// Gets the physical provider used to inspect or change backing entries independently of the binding.
            /// </summary>
            public PhysicalFileSystemProvider Physical { get; }

            /// <summary>
            /// Gets the observable provider injected into the directory binding.
            /// </summary>
            public Mock<IFileSystemProvider> Provider { get; } = new();

            /// <summary>
            /// Gets the logger used to verify refresh failures and commit-state diagnostics.
            /// </summary>
            public Mock<ILogger> Logger { get; } = new();

            /// <summary>
            /// Gets the session and namespace context for directory method calls.
            /// </summary>
            public SessionSystemContext Context { get; }

            /// <summary>
            /// Gets the directory node whose children and methods are populated by the binding.
            /// </summary>
            public FileDirectoryState Root { get; }

            /// <summary>
            /// Gets or sets the exception raised during enumeration after the first observed mutation.
            /// </summary>
            public Exception? RefreshFailure { get; set; }

            /// <summary>
            /// Gets or sets a callback invoked before enumeration to trigger controlled cancellation.
            /// </summary>
            public Action? BeforeEnumeration { get; set; }

            /// <summary>
            /// Gets the number of provider mutations admitted through the observable provider.
            /// </summary>
            public int MutationCount => m_mutationCount;

            /// <summary>
            /// Binds the root with an entry limit and optional asynchronous node-registration callback.
            /// </summary>
            public ValueTask<IFileDirectoryBinding> BindAsync(
                int maximum,
                Func<NodeState, CancellationToken, ValueTask>? registerNode = null)
            {
                return new FileDirectoryBinder().BindAsync(
                    Root, Provider.Object, Context, new FileDirectoryBindingOptions { MaxEntries = maximum },
                    registerNode);
            }

            /// <summary>
            /// Finds a materialized child directory and fails the test if it is absent.
            /// </summary>
            public FileDirectoryState FindDirectory(string name)
            {
                return (FileDirectoryState)(Root.FindChild(Context, new QualifiedName(name, 1)) ??
                    throw new AssertionException("The expected directory was not materialized."));
            }

            /// <summary>
            /// Finds a materialized child file and fails the test if it is absent.
            /// </summary>
            public FileState FindFile(string name)
            {
                return (FileState)(Root.FindChild(Context, new QualifiedName(name, 1)) ??
                    throw new AssertionException("The expected file was not materialized."));
            }

            /// <summary>
            /// Counts an admitted file creation before forwarding it to the physical provider.
            /// </summary>
            public ValueTask CreateFileAsync(string path, CancellationToken ct)
            {
                Interlocked.Increment(ref m_mutationCount);
                return Physical.CreateFileAsync(path, ct);
            }

            /// <summary>
            /// Verifies the exact number of logged refresh failures caused by exceeding the entry limit.
            /// </summary>
            public void VerifyRefreshLimitFailures(int count)
            {
                Logger.Verify(logger => logger.Log(
                    LogLevel.Error,
                    It.Is<EventId>(id => id.Name == "FileDirectoryRefreshFailed"),
                    It.IsAny<It.IsAnyType>(),
                    It.Is<Exception>(exception => exception is ServiceResultException &&
                        ((ServiceResultException)exception).StatusCode == StatusCodes.BadEncodingLimitsExceeded),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(count));
            }

            /// <summary>
            /// Verifies how many refresh failures were logged before or after the current provider mutation committed.
            /// </summary>
            public void VerifyRefreshCommitState(bool committed, int count)
            {
                Logger.Verify(logger => logger.Log(
                    LogLevel.Error,
                    It.Is<EventId>(id => id.Name == "FileDirectoryRefreshFailed"),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains("current provider mutation committed: " + committed)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(count));
            }

            /// <summary>
            /// Removes the temporary directory and all backing entries created by the test.
            /// </summary>
            public void Dispose()
            {
                Directory.Delete(m_path, recursive: true);
            }

            /// <summary>
            /// Enumerates the provider entries after running the configured refresh barrier or failure.
            /// </summary>
            private async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
                string path,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                BeforeEnumeration?.Invoke();
                if (MutationCount != 0 && RefreshFailure != null)
                {
                    throw RefreshFailure;
                }
                await foreach (FileSystemEntry entry in Physical.EnumerateAsync(path, ct).ConfigureAwait(false))
                {
                    yield return entry;
                }
            }

            /// <summary>
            /// Locates the temporary directory owned by this binding harness.
            /// </summary>
            private readonly string m_path;

            /// <summary>
            /// Counts provider mutations independently of address-space refresh success.
            /// </summary>
            private int m_mutationCount;
        }

        /// <summary>
        /// Invokes a callback during first disposal to inspect binding state from provider cleanup.
        /// </summary>
        private sealed class CleanupStream(Action onDispose) : MemoryStream(new byte[1])
        {
            /// <summary>
            /// Runs the inspection callback once before releasing the readable stream.
            /// </summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing && CanRead)
                {
                    onDispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
