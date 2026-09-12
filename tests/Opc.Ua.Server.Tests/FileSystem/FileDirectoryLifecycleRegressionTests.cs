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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    [TestFixture]
    [Category("FileSystem")]
    public sealed class FileDirectoryLifecycleRegressionTests
    {
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

        [Test]
        public async Task RefreshPublishesLookupOnlyAfterRegistrationCompletesAsync()
        {
            using var harness = new BindingHarness();
            await harness.Physical.CreateFileAsync("old", CancellationToken.None).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using IFileDirectoryBinding binding = await harness.BindAsync(2, async (_, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
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

        private sealed class BindingHarness : IDisposable
        {
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

            public PhysicalFileSystemProvider Physical { get; }
            public Mock<IFileSystemProvider> Provider { get; } = new();
            public Mock<ILogger> Logger { get; } = new();
            public SessionSystemContext Context { get; }
            public FileDirectoryState Root { get; }
            public Exception? RefreshFailure { get; set; }
            public int MutationCount => m_mutationCount;

            public ValueTask<IFileDirectoryBinding> BindAsync(
                int maximum,
                Func<NodeState, CancellationToken, ValueTask>? registerNode = null)
            {
                return new FileDirectoryBinder().BindAsync(
                    Root, Provider.Object, Context, new FileDirectoryBindingOptions { MaxEntries = maximum },
                    registerNode);
            }

            public FileDirectoryState FindDirectory(string name)
            {
                return (FileDirectoryState)(Root.FindChild(Context, new QualifiedName(name, 1)) ??
                    throw new AssertionException("The expected directory was not materialized."));
            }

            public FileState FindFile(string name)
            {
                return (FileState)(Root.FindChild(Context, new QualifiedName(name, 1)) ??
                    throw new AssertionException("The expected file was not materialized."));
            }

            public ValueTask CreateFileAsync(string path, CancellationToken ct)
            {
                Interlocked.Increment(ref m_mutationCount);
                return Physical.CreateFileAsync(path, ct);
            }

            public void Dispose()
            {
                Directory.Delete(m_path, recursive: true);
            }

            private async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
                string path,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                if (MutationCount != 0 && RefreshFailure != null)
                {
                    throw RefreshFailure;
                }
                await foreach (FileSystemEntry entry in Physical.EnumerateAsync(path, ct).ConfigureAwait(false))
                {
                    yield return entry;
                }
            }

            private readonly string m_path;
            private int m_mutationCount;
        }

        private sealed class CleanupStream(Action onDispose) : MemoryStream(new byte[1])
        {
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
