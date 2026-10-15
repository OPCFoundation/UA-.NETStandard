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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    public sealed partial class ProductionDatasetProtocolTests
    {
        /// <summary>
        /// Closing admission before signaling cancellation stops maintenance without a false cleanup failure.
        /// </summary>
        [Test]
        public async Task MaintenanceAdmissionClosesBeforeCancellationWithoutFailingDisposalAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ProductionDatasetBinding binding = await CreateShutdownBindingAsync(fixture).ConfigureAwait(false);
            Lock lifetimeLock = GetBindingField<Lock>(binding, "m_lifetimeLock");
            CancellationTokenSource stopping = GetBindingField<CancellationTokenSource>(binding, "m_stopping");
            Task maintenance = GetBindingField<Task>(binding, "m_maintenance");
            FieldInfo disposing = typeof(ProductionDatasetBinding).GetField(
                "m_disposing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                // Pin the real DisposeAsync admission state before CancelAsync without a production test hook.
                lock (lifetimeLock)
                {
                    disposing.SetValue(binding, true);
                }
                Assert.That(stopping.IsCancellationRequested, Is.False);
                AssertStatus(StatusCodes.BadShutdown, async () =>
                    await binding.ReclaimExpiredAsync().ConfigureAwait(false));
                AssertStatus(StatusCodes.BadShutdown, async () =>
                    await binding.RefreshAsync(fixture.Manager.SystemContext).ConfigureAwait(false));

                // Observe termination here; public disposal below must still surface a faulted worker.
                await Task.WhenAny(maintenance).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(stopping.IsCancellationRequested, Is.False);
            }
            finally
            {
                lock (lifetimeLock)
                {
                    disposing.SetValue(binding, false);
                }
                await binding.DisposeAsync().ConfigureAwait(false);
            }
            Assert.That(maintenance.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            await binding.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Disposal drains admitted maintenance before releasing every owned transfer and its metadata.
        /// </summary>
        [Test]
        public async Task DisposalDrainsAdmittedMaintenanceAndReclaimsTransfersExactlyOnceAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var files = new List<Mock<IFileTypeBinding>>();
            IFileTypeBinder binder = RecordFileBindings(files, async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            ProductionDatasetBinding binding = await CreateShutdownBindingAsync(fixture, binder).ConfigureAwait(false);
            var clientFiles = new List<UaTemporaryWriteFile>();
            ArrayOf<NodeId> resourceIds = default;
            Task? first = null;
            Task? second = null;
            try
            {
                var client = new ProductionDatasetClient(fixture.Session, binding.Node.NodeId);
                clientFiles.Add(await OpenShutdownTransferAsync(client, "first").ConfigureAwait(false));
                clientFiles.Add(await OpenShutdownTransferAsync(client, "second").ConfigureAwait(false));
                resourceIds = CaptureFileResourceIds(files);
                fixture.Clock.Advance(TimeSpan.FromSeconds(11));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                first = binding.DisposeAsync().AsTask();
                second = binding.DisposeAsync().AsTask();
                Assert.Multiple(() =>
                {
                    Assert.That(first.IsCompleted, Is.False, "An admitted cleanup still owns the first transfer.");
                    Assert.That(second.IsCompleted, Is.False, "Concurrent disposal must await the same cleanup.");
                    Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(files[0].Object.File.NodeId),
                        Is.Not.Null);
                });
                AssertStatus(StatusCodes.BadShutdown, async () =>
                    await binding.ReclaimExpiredAsync().ConfigureAwait(false));
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await binding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (first != null)
                    {
                        await first.ConfigureAwait(false);
                    }
                    if (second != null)
                    {
                        await second.ConfigureAwait(false);
                    }
                }
                finally
                {
                    foreach (UaTemporaryWriteFile file in clientFiles)
                    {
                        await file.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            Assert.That(files, Has.Count.EqualTo(2));
            foreach (Mock<IFileTypeBinding> file in files)
            {
                file.Verify(value => value.DisposeAsync(), Times.Once);
            }
            AssertShutdownResourcesReleased(fixture, binding, resourceIds);
        }

        /// <summary>
        /// A genuine maintenance failure remains visible even if shutdown cancels its operation concurrently.
        /// </summary>
        [TestCase("BadShutdown")]
        [TestCase("IoFailure")]
        [TestCase("Cancellation")]
        public async Task MaintenanceCleanupFailureDuringShutdownRemainsObservableAsync(string failureKind)
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception expected = failureKind switch
            {
                "BadShutdown" => new ServiceResultException(StatusCodes.BadShutdown, "Injected file cleanup failure."),
                "IoFailure" => new IOException("Injected file cleanup failure."),
                "Cancellation" => new OperationCanceledException("Injected file cleanup failure."),
                _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
            };
            var files = new List<Mock<IFileTypeBinding>>();
            IFileTypeBinder binder = RecordFileBindings(files, async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                throw expected;
            });
            ProductionDatasetBinding binding = await CreateShutdownBindingAsync(fixture, binder).ConfigureAwait(false);
            using CancellationTokenRegistration registration =
                GetBindingField<CancellationTokenSource>(binding, "m_stopping").Token
                    .Register(() => cancelled.TrySetResult(true));
            var clientFiles = new List<UaTemporaryWriteFile>();
            ArrayOf<NodeId> resourceIds = default;
            Task? first = null;
            try
            {
                var client = new ProductionDatasetClient(fixture.Session, binding.Node.NodeId);
                clientFiles.Add(await OpenShutdownTransferAsync(client, "failing").ConfigureAwait(false));
                clientFiles.Add(await OpenShutdownTransferAsync(client, "survivor").ConfigureAwait(false));
                resourceIds = CaptureFileResourceIds(files);
                fixture.Clock.Advance(TimeSpan.FromSeconds(11));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                first = binding.DisposeAsync().AsTask();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(first.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    AggregateException failure = Assert.ThrowsAsync<AggregateException>(async () =>
                        await binding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false))!;
                    Assert.That(failure.InnerExceptions, Is.EqualTo(new[] { expected }));
                    if (first != null)
                    {
                        AggregateException firstFailure = Assert.ThrowsAsync<AggregateException>(async () =>
                            await first.ConfigureAwait(false))!;
                        Assert.That(firstFailure, Is.SameAs(failure));
                    }
                }
                finally
                {
                    foreach (UaTemporaryWriteFile file in clientFiles)
                    {
                        await file.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            Assert.That(files, Has.Count.EqualTo(2));
            files[0].Verify(value => value.DisposeAsync(), Times.Exactly(2));
            files[1].Verify(value => value.DisposeAsync(), Times.Once);
            AssertShutdownResourcesReleased(fixture, binding, resourceIds);
        }

        /// <summary>
        /// Creates an independently owned binding so expected disposal failures cannot bypass host teardown.
        /// </summary>
        private static ValueTask<ProductionDatasetBinding> CreateShutdownBindingAsync(
            ProductionDatasetServerFixture fixture,
            IFileTypeBinder? fileBinder = null)
        {
            NodeState parent = fixture.Binding.Node.Parent
                ?? throw new InvalidOperationException("The fixture must supply an indexed parent.");
            ProductionDatasetManagementState node =
                fixture.Manager.SystemContext.CreateInstanceOfProductionDatasetManagementType(
                    parent,
                    new QualifiedName("ShutdownDatasets", fixture.Binding.Node.NodeId.NamespaceIndex));
            parent.AddChild(node);
            return ProductionDatasetBinding.CreateAsync(
                fixture.Manager, node, fixture.Provider, fixture.Policy,
                new ProductionDatasetOptions { TransferTimeout = TimeSpan.FromSeconds(10) },
                fileBinder, fixture.Clock);
        }

        /// <summary>
        /// Leaves a real session-owned upload and its uncommitted metadata for maintenance to reclaim.
        /// </summary>
        private static async Task<UaTemporaryWriteFile> OpenShutdownTransferAsync(
            ProductionDatasetClient client,
            string name)
        {
            UaTemporaryWriteFile file = await client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, name)).ConfigureAwait(false);
            try
            {
                await WriteByteAsync(file.Stream, 1).ConfigureAwait(false);
                await client.SendInformationAsync(file.Handle,
                    new ProductionDatasetInformationType { Name = name, Components = [0] }).ConfigureAwait(false);
                return file;
            }
            catch
            {
                await file.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Delegates real stream ownership to FileTypeBinder while gating only the first disposal outcome.
        /// </summary>
        private static IFileTypeBinder RecordFileBindings(
            List<Mock<IFileTypeBinding>> files,
            Func<ValueTask> afterFirstDispose)
        {
            int completed = 0;
            var binder = new Mock<IFileTypeBinder>(MockBehavior.Strict);
            binder.Setup(value => value.Bind(
                It.IsAny<FileState>(), It.IsAny<ISystemContext>(), It.IsAny<ByteString>(),
                It.IsAny<Func<ByteString, CancellationToken, ValueTask>?>(),
                It.IsAny<bool>(), It.IsAny<NodeId>(), It.IsAny<int>()))
                .Returns((FileState file, ISystemContext context, ByteString content,
                    Func<ByteString, CancellationToken, ValueTask>? commit,
                    bool commitOnClose, NodeId owner, int maximumBytes) =>
                {
                    IFileTypeBinding inner = new FileTypeBinder().Bind(
                        file, context, content, commit, commitOnClose, owner, maximumBytes);
                    int disposed = 0;
                    var binding = new Mock<IFileTypeBinding>(MockBehavior.Strict);
                    binding.SetupGet(value => value.File).Returns(file);
                    binding.Setup(value => value.OpenAsync(
                        It.IsAny<NodeId>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                        .Returns((NodeId session, byte mode, CancellationToken ct) =>
                            inner.OpenAsync(session, mode, ct));
                    binding.Setup(value => value.CloseAsync(
                        It.IsAny<NodeId>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                        .Returns((NodeId session, uint handle, CancellationToken ct) =>
                            inner.CloseAsync(session, handle, ct));
                    binding.Setup(value => value.DisposeAsync()).Returns(async () =>
                    {
                        if (Interlocked.Exchange(ref disposed, 1) == 0)
                        {
                            await inner.DisposeAsync().ConfigureAwait(false);
                            Assert.That(file.OpenCount!.Value, Is.Zero,
                                "The real binder must close its stream before transfer nodes are removed.");
                            if (Interlocked.Increment(ref completed) == 1)
                            {
                                await afterFirstDispose().ConfigureAwait(false);
                            }
                        }
                    });
                    files.Add(binding);
                    return binding.Object;
                });
            return binder.Object;
        }

        /// <summary>
        /// Captures identifiers before node deletion clears the generated child-property references.
        /// </summary>
        private static ArrayOf<NodeId> CaptureFileResourceIds(List<Mock<IFileTypeBinding>> files)
        {
            var identifiers = new List<NodeId>();
            foreach (Mock<IFileTypeBinding> recorded in files)
            {
                FileState file = recorded.Object.File;
                identifiers.Add(file.NodeId);
                identifiers.Add(file.Size!.NodeId);
                identifiers.Add(file.OpenCount!.NodeId);
            }
            return identifiers.ToArrayOf();
        }

        /// <summary>
        /// Verifies node and property removal, metadata release, and no authority commit.
        /// </summary>
        private static void AssertShutdownResourcesReleased(
            ProductionDatasetServerFixture fixture,
            ProductionDatasetBinding binding,
            ArrayOf<NodeId> resourceIds)
        {
            Assert.That(resourceIds.Count, Is.EqualTo(6));
            foreach (NodeId nodeId in resourceIds)
            {
                Assert.That(nodeId.IsNull, Is.False);
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(nodeId), Is.Null);
            }
            Assert.That(GetBindingField<IDictionary>(binding, "m_transfers"), Is.Empty);
            Assert.That(fixture.Provider.CommitCount, Is.Zero);
        }

        /// <summary>
        /// Inspects the existing lifecycle boundary without adding a production hook or scheduler.
        /// </summary>
        private static T GetBindingField<T>(ProductionDatasetBinding binding, string name)
        {
            return (T)typeof(ProductionDatasetBinding)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding)!;
        }
    }
}
