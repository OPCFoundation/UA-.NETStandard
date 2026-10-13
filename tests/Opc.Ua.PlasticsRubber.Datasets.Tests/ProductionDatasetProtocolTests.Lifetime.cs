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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    public sealed partial class ProductionDatasetProtocolTests
    {
        /// <summary>
        /// A failed authority commit preserves the old committed state and leaves the upload retryable.
        /// </summary>
        [Test]
        public async Task CommitFailureDoesNotPublishOrPersistAndTheSameHandleCanBeRetriedAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "old", StorageEnumeration.PRODUCTION, [1], [0]).ConfigureAwait(false);
            UaTemporaryWriteFile file = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.PRODUCTION | StorageEnumeration.FILE_SYSTEM,
                    "new")).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                await WriteByteAsync(file.Stream, 2).ConfigureAwait(false);
                await fixture.Client.SendInformationAsync(file.Handle,
                    new ProductionDatasetInformationType { Name = "new", Components = [0] }).ConfigureAwait(false);
                fixture.Provider.BeforeCommit = (_, _) => throw new ServiceResultException(StatusCodes.BadOutOfService);
                AssertStatus(StatusCodes.BadOutOfService, async () => await file.CommitAsync().ConfigureAwait(false));
                Assert.That((await fixture.Client.ReadInformationAsync(StorageEnumeration.PRODUCTION)
                    .ConfigureAwait(false)).Name, Is.EqualTo("old"));
                Assert.That((await fixture.Client.GetListAsync("new").ConfigureAwait(false)).IsEmpty, Is.True);
                Assert.That(fixture.Provider.CommitCount, Is.EqualTo(1));
                fixture.Provider.BeforeCommit = null;
                Assert.That((await file.CommitAsync().ConfigureAwait(false)).IsNull, Is.True);
                Assert.That((await fixture.Client.ReadInformationAsync(StorageEnumeration.PRODUCTION)
                    .ConfigureAwait(false)).Name, Is.EqualTo("new"));
                Assert.That((await fixture.Client.GetListAsync("new").ConfigureAwait(false)).Count, Is.EqualTo(1));
            }
            AssertStatus(StatusCodes.BadInvalidArgument, async () => await fixture.Client.SendInformationAsync(
                file.Handle, new ProductionDatasetInformationType { Name = "new" }).ConfigureAwait(false));
        }

        /// <summary>
        /// A commit reply waits for the injected authority, and plain Close discards uncommitted bytes.
        /// </summary>
        [Test]
        public async Task CommitAcknowledgementWaitsForAuthorityAndUncommittedCloseRollsBackAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Provider.BeforeCommit = async (_, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            UaTemporaryWriteFile file = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "committed")).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                await WriteByteAsync(file.Stream, 5).ConfigureAwait(false);
                Task<NodeId> commit = file.CommitAsync().AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(commit.IsCompleted, Is.False);
                    Assert.That(fixture.Provider.CommitCount, Is.Zero);
                });
                release.TrySetResult(true);
                Assert.That((await commit.ConfigureAwait(false)).IsNull, Is.True);
            }
            UaTemporaryWriteFile abandoned = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "abandoned")).ConfigureAwait(false);
            await WriteByteAsync(abandoned.Stream, 6).ConfigureAwait(false);
            await abandoned.DisposeAsync().ConfigureAwait(false);
            await abandoned.DisposeAsync().ConfigureAwait(false);
            Assert.That((await fixture.Client.GetListAsync("abandoned").ConfigureAwait(false)).IsEmpty, Is.True);
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(abandoned.FileNodeId), Is.Null);
        }

        /// <summary>
        /// A deterministic timeout reclaims files and their metadata without committing staged bytes.
        /// </summary>
        [Test]
        public async Task ExpiryAndSessionShutdownReclaimEveryTemporaryNodeAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            UaTemporaryWriteFile expired = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "expired")).ConfigureAwait(false);
            await WriteByteAsync(expired.Stream, 9).ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromSeconds(11));
            await fixture.Binding.ReclaimExpiredAsync().ConfigureAwait(false);
            AssertStatus(StatusCodes.BadInvalidArgument, async () => await expired.CommitAsync().ConfigureAwait(false));
            await expired.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(expired.FileNodeId), Is.Null);
            Assert.That((await fixture.Client.GetListAsync().ConfigureAwait(false)).IsEmpty, Is.True);

            Opc.Ua.Client.ISession session = await fixture.ConnectSessionAsync().ConfigureAwait(false);
            var client = new ProductionDatasetClient(session, fixture.Binding.Node.NodeId);
            UaTemporaryWriteFile closed = await client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "session-closed")).ConfigureAwait(false);
            await session.CloseAsync().ConfigureAwait(false);
            await fixture.Binding.ReclaimExpiredAsync().ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(closed.FileNodeId), Is.Null);
            await closed.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Provider.CommitCount, Is.Zero);
        }

        /// <summary>
        /// The transfer cap includes pending uploads and releasing one slot permits another.
        /// </summary>
        [Test]
        public async Task ConcurrentHandleCountAndFileBytesAreBoundedOnTheWireAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var files = new List<UaTemporaryWriteFile>();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    files.Add(await fixture.Client.OpenWriteAsync(
                        WriteOptions(StorageEnumeration.FILE_SYSTEM, "pending-" + i)).ConfigureAwait(false));
                }
                AssertStatus(StatusCodes.BadTooManyOperations, async () => await fixture.Client.OpenWriteAsync(
                    WriteOptions(StorageEnumeration.FILE_SYSTEM, "overflow")).ConfigureAwait(false));
                var file = new FileTypeClient(
                    fixture.Session, files[0].FileNodeId, fixture.Session.MessageContext.Telemetry);
                AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () =>
                    await file.WriteAsync(files[0].Handle, ByteString.From(new byte[1025])).ConfigureAwait(false));
                await files[0].DisposeAsync().ConfigureAwait(false);
                files.Add(await fixture.Client.OpenWriteAsync(
                    WriteOptions(StorageEnumeration.FILE_SYSTEM, "replacement")).ConfigureAwait(false));
            }
            finally
            {
                foreach (UaTemporaryWriteFile file in files)
                {
                    await file.DisposeAsync().ConfigureAwait(false);
                }
            }
            Assert.That(fixture.Provider.CommitCount, Is.Zero);
        }

        /// <summary>
        /// Client byte bounds stop non-seekable transfers without committing a partial upload.
        /// </summary>
        [Test]
        public async Task ClientBoundsAbortOversizedUploadsAndCloseOversizedDownloadsAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var client = new ProductionDatasetClient(fixture.Session, fixture.Binding.Node.NodeId,
                new ProductionDatasetClientOptions { MaximumFileBytes = 3, ChunkSize = 2 });
            using var input = new MemoryStream([1, 2, 3, 4]);
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () => await client.UploadAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "oversized"), input).ConfigureAwait(false));
            Assert.That((await fixture.Client.GetListAsync("oversized").ConfigureAwait(false)).IsEmpty, Is.True);
            await UploadAsync(fixture, "large", StorageEnumeration.FILE_SYSTEM, [1, 2, 3, 4], [0, 1, 1, 2])
                .ConfigureAwait(false);
            using var destination = new MemoryStream();
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () => await client.DownloadContentAsync(
                ReadOptions(StorageEnumeration.FILE_SYSTEM, "large"), destination).ConfigureAwait(false));
            await fixture.Binding.ReclaimExpiredAsync().ConfigureAwait(false);
            var children = new List<BaseInstanceState>();
            fixture.Binding.Node.ProductionDatasetTransfer!.GetChildren(fixture.Manager.SystemContext, children);
            Assert.That(children.FindAll(child => child is FileState), Is.Empty);
        }

        /// <summary>
        /// Provider cancellation is bounded by the server deadline and does not commit staged state.
        /// </summary>
        [Test]
        public async Task ServerDeadlineCancelsProviderAndExplicitClientCancellationDoesNotAllocateAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture(new ProductionDatasetOptions
            {
                OperationTimeout = TimeSpan.FromMilliseconds(150)
            });
            await fixture.StartAsync().ConfigureAwait(false);
            bool canceled = false;
            fixture.Provider.BeforeCommit = async (_, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    throw;
                }
            };
            using var content = new MemoryStream([1]);
            AssertStatus(StatusCodes.BadTimeout, async () => await fixture.Client.UploadAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "deadline"), content).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(canceled, Is.True);
                Assert.That(fixture.Provider.CommitCount, Is.Zero);
            });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(async () => await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "canceled"), cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That((await fixture.Client.GetListAsync().ConfigureAwait(false)).IsEmpty, Is.True);
        }

        /// <summary>
        /// Concurrent disposal cancels an in-flight authority before draining queued operations and cleanup.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposeCancelsCommitAndDrainsQueuedOperationsBeforeReturningAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Provider.BeforeCommit = async (_, ct) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            };
            UaTemporaryWriteFile file = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.PRODUCTION, "in-flight")).ConfigureAwait(false);
            await WriteByteAsync(file.Stream, 1).ConfigureAwait(false);
            Task<NodeId> commit = file.CommitAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<ProductionDatasetInformationType> read =
                fixture.Client.ReadInformationAsync(StorageEnumeration.PRODUCTION)
                .AsTask();
            Task first = fixture.Binding.DisposeAsync().AsTask();
            Task second = fixture.Binding.DisposeAsync().AsTask();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            AssertStatus(StatusCodes.BadShutdown, async () => await commit.ConfigureAwait(false));
            AssertStatus(StatusCodes.BadShutdown, async () => await read.ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Provider.CommitCount, Is.Zero);
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(file.FileNodeId), Is.Null);
            });
            await file.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Optional capabilities remain absent and mandatory transfers still work without information methods.
        /// </summary>
        [Test]
        public async Task MandatoryOnlyBindingRejectsPreparationWithoutInventingOptionalCapabilitiesAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture(
                new ProductionDatasetOptions { Features = ProductionDatasetFeatures.None });
            await fixture.StartAsync().ConfigureAwait(false);
            using var content = new MemoryStream([4]);
            await fixture.Client.UploadAsync(
                WriteOptions(StorageEnumeration.PRODUCTION | StorageEnumeration.FILE_SYSTEM, "mandatory"), content)
                .ConfigureAwait(false);
            using var output = new MemoryStream();
            await fixture.Client.DownloadContentAsync(ReadOptions(StorageEnumeration.PRODUCTION), output)
                .ConfigureAwait(false);
            Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 4 }));
            AssertStatus(StatusCodes.BadNotSupported, async ()
                => await fixture.Client.GetListAsync().ConfigureAwait(false));
            AssertStatus(StatusCodes.BadNotSupported, async () =>
                await fixture.Client.ReadFrozenAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false));
            AssertStatus(StatusCodes.BadNotSupported, async () => await fixture.Client.OpenReadAsync(
                ReadOptions(StorageEnumeration.PREPARATION)).ConfigureAwait(false));
            AssertStatus(StatusCodes.BadNotSupported, async () => await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.PREPARATION | StorageEnumeration.FILE_SYSTEM, "unsupported"))
                .ConfigureAwait(false));
            Assert.That(fixture.Binding.Node.ProductionDatasetInPreparationStatus, Is.Null);
            Assert.That(fixture.Binding.Node.GetProductionDatasetInformation, Is.Null);
        }

        /// <summary>
        /// A family materializer's asymmetric layer selection is preserved exactly.
        /// </summary>
        [Test]
        public async Task ExistingSelectionDoesNotCreateExcludedPerLayerMethodsOrPropertiesAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture(
                new ProductionDatasetOptions { PreserveExistingSelection = true }, (node, context) =>
                {
                    node.ActiveProductionDatasetStatus!.AddFrozen(context);
                    node.AddProductionDatasetInPreparationStatus(context);
                    node.ProductionDatasetInPreparationStatus!.AddModified(context);
                    node.ProductionDatasetInPreparationStatus.AddLoad(context);
                    node.AddSendProductionDatasetInformation(context);
                });
            await fixture.StartAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Binding.Node.ActiveProductionDatasetStatus!.Load, Is.Null);
                Assert.That(fixture.Binding.Node.ActiveProductionDatasetStatus.Save, Is.Null);
                Assert.That(fixture.Binding.Node.ActiveProductionDatasetStatus.Modified, Is.Null);
                Assert.That(fixture.Binding.Node.ProductionDatasetInPreparationStatus!.Frozen, Is.Null);
                Assert.That(fixture.Binding.Node.ProductionDatasetInPreparationStatus.Save, Is.Null);
                Assert.That(fixture.Binding.Node.GetProductionDatasetInformation, Is.Null);
                Assert.That(fixture.Binding.Node.ProductionDatasetLists, Is.Null);
            });
            await UploadAsync(fixture, "selected", StorageEnumeration.FILE_SYSTEM, [6], [6]).ConfigureAwait(false);
            await fixture.Client.LoadAsync(StorageEnumeration.PREPARATION, "selected", []).ConfigureAwait(false);
            Assert.That((await fixture.Client.ReadInformationAsync(StorageEnumeration.PREPARATION)
                .ConfigureAwait(false)).Name, Is.EqualTo("selected"));
            await fixture.Client.SetFrozenAsync(StorageEnumeration.PRODUCTION, true).ConfigureAwait(false);
            Assert.That(await fixture.Client.ReadFrozenAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false),
                Is.True);
            AssertStatus(StatusCodes.BadNotSupported, async () =>
                await fixture.Client.ReadFrozenAsync(StorageEnumeration.PREPARATION).ConfigureAwait(false));
        }
    }
}
