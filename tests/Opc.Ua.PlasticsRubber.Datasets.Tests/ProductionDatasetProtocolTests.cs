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
    /// <summary>
    /// Exercises chapter 20 through generated clients and a real UA service connection.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Integration")]
    public sealed partial class ProductionDatasetProtocolTests
    {
        /// <summary>
        /// Acknowledges a multi-destination upload only after the injected authority commits it.
        /// </summary>
        [Test]
        public async Task UploadThenLoadAndDownloadPreservesInformationAndRepeatedComponentsAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            using var input = new MemoryStream([7, 8, 9]);
            var information = new ProductionDatasetInformationType
            {
                Name = "recipe-300",
                Description = "Three components",
                Components = [0, 1, 1],
                MouldId = "mould-a"
            };
            await fixture.Client.UploadAsync(new ProductionDatasetWriteOptionsType
            {
                Storage = StorageEnumeration.FILE_SYSTEM | StorageEnumeration.PREPARATION,
                Name = "recipe-300",
                Components = []
            }, input, information).ConfigureAwait(false);
            await fixture.Client.LoadAsync(StorageEnumeration.PRODUCTION, "recipe-300", [])
                .ConfigureAwait(false);
            using var output = new MemoryStream();
            ProductionDatasetInformationType downloaded = await fixture.Client.DownloadAsync(
                new ProductionDatasetReadOptionsType
                {
                    Storage = StorageEnumeration.PRODUCTION,
                    Name = string.Empty
                }, output).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 7, 8, 9 }));
                Assert.That(downloaded.Name, Is.EqualTo("recipe-300"));
                Assert.That(downloaded.Components.ToArray(), Is.EqualTo(new ushort[] { 0, 1, 1 }));
                Assert.That(fixture.Provider.ActivationCount, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Load performs actual component-selective activation and Save resets the committed modification flag.
        /// </summary>
        [Test]
        public async Task PartialLoadChangesOnlyRequestedComponentsAndSavePersistsModifiedParametersAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "baseline", StorageEnumeration.PRODUCTION, [1, 2, 3], [0, 1, 1])
                .ConfigureAwait(false);
            await UploadAsync(fixture, "replacement", StorageEnumeration.FILE_SYSTEM, [7, 8, 9], [0, 1, 1])
                .ConfigureAwait(false);
            await fixture.Client.LoadAsync(StorageEnumeration.PRODUCTION, "replacement", [1]).ConfigureAwait(false);
            using var selected = new MemoryStream();
            await fixture.Client.DownloadContentAsync(ReadOptions(StorageEnumeration.PRODUCTION), selected)
                .ConfigureAwait(false);
            Assert.That(selected.ToArray(), Is.EqualTo(new byte[] { 1, 8, 9 }));
            Assert.That(await fixture.Client.ReadModifiedAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false),
                Is.True);

            fixture.Provider.Modify(StorageEnumeration.PRODUCTION, ByteString.From(new byte[] { 5, 8, 9 }));
            ProductionDatasetInformationType edited = await
                fixture.Client.ReadInformationAsync(StorageEnumeration.PRODUCTION)
                .ConfigureAwait(false);
            Assert.That(edited.UserName, Is.EqualTo("test-operator"));
            await fixture.Client.SaveAsync(StorageEnumeration.PRODUCTION, "saved").ConfigureAwait(false);
            Assert.That(await fixture.Client.ReadModifiedAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false),
                Is.False);
            using var saved = new MemoryStream();
            ProductionDatasetInformationType information = await fixture.Client.DownloadAsync(
                ReadOptions(StorageEnumeration.FILE_SYSTEM, "saved"), saved).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(saved.ToArray(), Is.EqualTo(new byte[] { 5, 8, 9 }));
                Assert.That(information.Name, Is.EqualTo("saved"));
                Assert.That(information.Components.ToArray(), Is.EqualTo(new ushort[] { 0, 1, 1 }));
                Assert.That(information.LastSaveTimestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
            });
        }

        /// <summary>
        /// Every nonzero destination combination commits every requested destination.
        /// </summary>
        [Test]
        public async Task EveryPublishedWriteStorageMaskCommitsAllSelectedDestinationsAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            for (int mask = 1; mask <= 7; mask++)
            {
                var storage = (StorageEnumeration)mask;
                string name = "mask-" + mask;
                await UploadAsync(fixture, name, storage, [(byte)mask], [0]).ConfigureAwait(false);
                foreach (StorageEnumeration destination in new[]
                {
                    StorageEnumeration.PRODUCTION, StorageEnumeration.PREPARATION, StorageEnumeration.FILE_SYSTEM
                })
                {
                    if ((storage & destination) == 0)
                    {
                        continue;
                    }
                    using var content = new MemoryStream();
                    await fixture.Client.DownloadContentAsync(
                        ReadOptions(destination, destination == StorageEnumeration.FILE_SYSTEM
                            ? name : string.Empty), content)
                        .ConfigureAwait(false);
                    Assert.That(
                        content.ToArray(), Is.EqualTo(new byte[] { (byte)mask }), $"Mask {mask}, {destination}");
                }
            }
        }

        /// <summary>
        /// Read storage is a single source, names are source-dependent, and write storage is a validated mask.
        /// </summary>
        [Test]
        public async Task InvalidOptionsAndWrongGeneratedTypesAreRejectedOverUaAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            TemporaryFileTransferClient transfer = await fixture.Client.GetTransferClientAsync().ConfigureAwait(false);
            foreach (int storage in new[] { 0, 3, 5, 6, 7, 8, -1 })
            {
                await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForReadAsync(
                    Variant.FromStructure(ReadOptions((StorageEnumeration)storage))).ConfigureAwait(false));
            }
            foreach (StorageEnumeration storage in new[] { StorageEnumeration.PRODUCTION,
                StorageEnumeration.PREPARATION })
            {
                await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForReadAsync(
                    Variant.FromStructure(ReadOptions(storage, "not-empty"))).ConfigureAwait(false));
            }
            await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForReadAsync(
                Variant.FromStructure(ReadOptions(StorageEnumeration.FILE_SYSTEM))).ConfigureAwait(false));
            foreach (int storage in new[] { 0, 8, 9, -1 })
            {
                await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForWriteAsync(
                    Variant.FromStructure(WriteOptions((StorageEnumeration)storage, "invalid"))).ConfigureAwait(false));
            }
            await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForWriteAsync(
                Variant.FromStructure(WriteOptions(StorageEnumeration.PRODUCTION,
                    string.Empty))).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForWriteAsync(
                Variant.FromStructure(new ProductionDatasetWriteOptionsType
                {
                    Name = "invalid-components", Storage = StorageEnumeration.FILE_SYSTEM, Components = [1]
                })).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadOutOfRange, async () => await transfer.Proxy.GenerateFileForWriteAsync(
                Variant.FromStructure(new ProductionDatasetWriteOptionsType
                {
                    Name = "invalid-code", Storage = StorageEnumeration.PRODUCTION, Components = [16]
                })).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadInvalidArgument, async () => await transfer.Proxy.GenerateFileForReadAsync(
                Variant.FromStructure(WriteOptions(StorageEnumeration.FILE_SYSTEM,
                    "wrong-type"))).ConfigureAwait(false));
            CallResponse mismatch = await fixture.Session.CallAsync(null,
            [
                new CallMethodRequest
                {
                    ObjectId = fixture.Binding.Node.ProductionDatasetTransfer!.NodeId,
                    MethodId = fixture.Binding.Node.ProductionDatasetTransfer.GenerateFileForRead!.NodeId,
                    InputArguments =
                    [
                        Variant.FromStructure(WriteOptions(StorageEnumeration.FILE_SYSTEM, "wrong-type"))
                    ]
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(mismatch.Results[0].InputArgumentResults[0], Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(fixture.Provider.CommitCount, Is.Zero);
        }

        /// <summary>
        /// The active and preparation frozen policies independently prevent mutations and are authoritatively writable.
        /// </summary>
        [Test]
        public async Task FrozenControlLayerBlocksLoadSaveWriteAndCommitUntilAuthorizedUnfreezeAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "initial",
                StorageEnumeration.PRODUCTION | StorageEnumeration.PREPARATION | StorageEnumeration.FILE_SYSTEM,
                [1], [0]).ConfigureAwait(false);
            UaTemporaryWriteFile pending = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.PRODUCTION, "pending")).ConfigureAwait(false);
            await using (pending.ConfigureAwait(false))
            {
                await WriteByteAsync(pending.Stream, 2).ConfigureAwait(false);
                await fixture.Client.SetFrozenAsync(StorageEnumeration.PRODUCTION, true).ConfigureAwait(false);
                Assert.That(await
                    fixture.Client.ReadFrozenAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false), Is.True);
                await AssertStatus(StatusCodes.BadInvalidState, async () =>
                    await fixture.Client.LoadAsync(StorageEnumeration.PRODUCTION, "initial", []).ConfigureAwait(false));
                await AssertStatus(StatusCodes.BadInvalidState, async () =>
                    await fixture.Client.SaveAsync(StorageEnumeration.PRODUCTION, "saved").ConfigureAwait(false));
                await AssertStatus(StatusCodes.BadInvalidState, async () =>
                    await fixture.Client.OpenWriteAsync(WriteOptions(StorageEnumeration.PRODUCTION, "new"))
                        .ConfigureAwait(false));
                await AssertStatus(StatusCodes.BadInvalidState, async () =>
                    await WriteByteAsync(pending.Stream, 3).ConfigureAwait(false));
                await AssertStatus(StatusCodes.BadInvalidState, async ()
                    => await pending.CommitAsync().ConfigureAwait(false));
                await fixture.Client.LoadAsync(StorageEnumeration.PREPARATION, "initial", []).ConfigureAwait(false);
                using var readable = new MemoryStream();
                await fixture.Client.DownloadContentAsync(ReadOptions(StorageEnumeration.PRODUCTION), readable)
                    .ConfigureAwait(false);
                Assert.That(readable.ToArray(), Is.EqualTo(new byte[] { 1 }));
                await fixture.Client.SetFrozenAsync(StorageEnumeration.PRODUCTION, false).ConfigureAwait(false);
                await pending.CommitAsync().ConfigureAwait(false);
            }
            Assert.That((await fixture.Client.ReadInformationAsync(StorageEnumeration.PRODUCTION)
                .ConfigureAwait(false)).Name, Is.EqualTo("pending"));
        }

        /// <summary>
        /// Lists support exact names, both wildcards, mould filters and empty client responses.
        /// </summary>
        [Test]
        public async Task DatasetListsFilterAndRetainRepeatedCodesAndSessionOwnedEmptyResponsesAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "300", StorageEnumeration.FILE_SYSTEM, [1, 2, 3], [0, 1, 1], "a")
                .ConfigureAwait(false);
            await UploadAsync(fixture, "301", StorageEnumeration.FILE_SYSTEM, [4], [6], "b").ConfigureAwait(false);
            await UploadAsync(fixture, "400", StorageEnumeration.FILE_SYSTEM, [5], [6], "a").ConfigureAwait(false);
            Assert.That((await fixture.Client.GetListAsync().ConfigureAwait(false)).Count, Is.EqualTo(3));
            Assert.That((await fixture.Client.GetListAsync("30?", string.Empty).ConfigureAwait(false)).Count,
                Is.EqualTo(2));
            ArrayOf<ProductionDatasetInformationType> selected = await fixture.Client.GetListAsync("3*", "a")
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(selected.Count, Is.EqualTo(1));
                Assert.That(selected[0].Name, Is.EqualTo("300"));
                Assert.That(selected[0].Components.ToArray(), Is.EqualTo(new ushort[] { 0, 1, 1 }));
            });
            Assert.That((await fixture.Client.GetListAsync("300").ConfigureAwait(false)).Count, Is.EqualTo(1));
            Assert.That((await fixture.Client.GetListAsync("missing").ConfigureAwait(false)).IsEmpty, Is.True);
            await fixture.Client.SendListAsync(selected).ConfigureAwait(false);
            Assert.That(fixture.Provider.ReadClientList(fixture.Session.SessionId).Count, Is.EqualTo(1));
            await fixture.Client.SendListAsync([]).ConfigureAwait(false);
            Assert.That(fixture.Provider.ReadClientList(fixture.Session.SessionId).IsEmpty, Is.True);
        }

        /// <summary>
        /// Metadata and file operations stay bound to the generated handle, direction and real session.
        /// </summary>
        [Test]
        public async Task ForeignSessionsWrongDirectionsAndCrossFileHandlesCannotAccessTransfersAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "one", StorageEnumeration.FILE_SYSTEM, [1], [0]).ConfigureAwait(false);
            TemporaryFileTransferClient transfers = await fixture.Client.GetTransferClientAsync().ConfigureAwait(false);
            (NodeId firstId, uint first, _) = await transfers.Proxy.GenerateFileForReadAsync(
                Variant.FromStructure(ReadOptions(StorageEnumeration.FILE_SYSTEM, "one"))).ConfigureAwait(false);
            (NodeId secondId, uint second, _) = await transfers.Proxy.GenerateFileForReadAsync(
                Variant.FromStructure(ReadOptions(StorageEnumeration.FILE_SYSTEM, "one"))).ConfigureAwait(false);
            Assert.That(first, Is.Not.EqualTo(second));
            var firstFile = new FileTypeClient(fixture.Session, firstId, fixture.Session.MessageContext.Telemetry);
            var secondFile = new FileTypeClient(fixture.Session, secondId, fixture.Session.MessageContext.Telemetry);
            Opc.Ua.Client.ISession foreign = await fixture.ConnectSessionAsync().ConfigureAwait(false);
            var foreignClient = new ProductionDatasetClient(foreign, fixture.Binding.Node.NodeId);
            var foreignFile = new FileTypeClient(foreign, firstId, foreign.MessageContext.Telemetry);
            await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                await foreignClient.GetInformationAsync(first).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                await foreignFile.ReadAsync(first, 1).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadInvalidArgument, async () =>
                await firstFile.ReadAsync(second, 1).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadInvalidState, async () => await firstFile.OpenAsync(1).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadInvalidArgument, async () =>
                await transfers.Proxy.CloseAndCommitAsync(first).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadInvalidArgument, async () =>
                await fixture.Client.SendInformationAsync(first, new ProductionDatasetInformationType { Name = "one" })
                    .ConfigureAwait(false));
            await firstFile.CloseAsync(first).ConfigureAwait(false);
            await AssertStatus(StatusCodes.BadInvalidArgument, async () =>
                await fixture.Client.GetInformationAsync(first).ConfigureAwait(false));
            Assert.That((await secondFile.ReadAsync(second, 1).ConfigureAwait(false)).ToArray(),
                Is.EqualTo(new byte[] { 1 }));
            await secondFile.CloseAsync(second).ConfigureAwait(false);

            UaTemporaryWriteFile writer = await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, "writer")).ConfigureAwait(false);
            await using (writer.ConfigureAwait(false))
            {
                await AssertStatus(StatusCodes.BadInvalidArgument, async () =>
                    await fixture.Client.GetInformationAsync(writer.Handle).ConfigureAwait(false));
                await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                    await foreignClient.SendInformationAsync(
                        writer.Handle, new ProductionDatasetInformationType { Name = "writer" }).ConfigureAwait(false));
            }
            await AssertStatus(StatusCodes.BadInvalidArgument, async () =>
                await fixture.Client.SendInformationAsync(
                    writer.Handle, new ProductionDatasetInformationType { Name = "writer" }).ConfigureAwait(false));
        }

        /// <summary>
        /// Authorization is re-evaluated for every read, mutation and transfer operation.
        /// </summary>
        [Test]
        public async Task PolicyDenialAndOriginalCommitIdentityArePreservedAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "identity", StorageEnumeration.PRODUCTION, [3], [0]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(((ISessionSystemContext)fixture.Provider.LastCommitRequest.SystemContext).SessionId,
                    Is.EqualTo(fixture.Session.SessionId));
                Assert.That(((ISessionSystemContext)fixture.Provider.LastCommitRequest.SystemContext).UserIdentity,
                    Is.SameAs(((ISessionSystemContext)fixture.Policy.LastRequest.SystemContext).UserIdentity));
                Assert.That(fixture.Provider.LastCommitRequest.TargetId,
                    Is.EqualTo(fixture.Binding.Node.ProductionDatasetTransfer!.NodeId));
            });
            fixture.Policy.DeniedMember = fixture.Binding.Node.ActiveProductionDatasetStatus!.Frozen!.NodeId;
            await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                await fixture.Client.SetFrozenAsync(StorageEnumeration.PRODUCTION, true).ConfigureAwait(false));
            fixture.Policy.DenyAll = true;
            await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                await fixture.Client.ReadInformationAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                await fixture.Client.GetListAsync().ConfigureAwait(false));
            await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                await fixture.Client.OpenReadAsync(ReadOptions(StorageEnumeration.PRODUCTION)).ConfigureAwait(false));
        }

        /// <summary>
        /// Exercises real provider persistence without duplicating file transfer implementation in tests.
        /// </summary>
        private static async Task UploadAsync(
            ProductionDatasetServerFixture fixture,
            string name,
            StorageEnumeration storage,
            byte[] content,
            ushort[] components,
            string mouldId = "")
        {
            using var source = new MemoryStream(content);
            await fixture.Client.UploadAsync(
                WriteOptions(storage, name), source,
                new ProductionDatasetInformationType
                {
                    Name = name,
                    Components = components.ToArrayOf(),
                    MouldId = mouldId
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a generated read-options value with an explicitly selected source.
        /// </summary>
        private static ProductionDatasetReadOptionsType ReadOptions(StorageEnumeration storage, string name = "")
        {
            return new ProductionDatasetReadOptionsType { Storage = storage, Name = name };
        }

        /// <summary>
        /// Creates a generated write-options value with complete-dataset activation.
        /// </summary>
        private static ProductionDatasetWriteOptionsType WriteOptions(StorageEnumeration storage, string name)
        {
            return new ProductionDatasetWriteOptionsType { Storage = storage, Name = name, Components = [] };
        }

        /// <summary>
        /// Asserts the original UA failure status rather than merely expecting any exception.
        /// </summary>
        private static async Task AssertStatus(StatusCode status, Func<Task> action)
        {
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(action);
            Assert.That(exception!.StatusCode, Is.EqualTo(status));
        }

        /// <summary>
        /// Writes a test byte with the native asynchronous stream overload for each target framework.
        /// </summary>
        private static ValueTask WriteByteAsync(Stream stream, byte value)
        {
            byte[] content = [value];
#if NETFRAMEWORK
            return new ValueTask(stream.WriteAsync(content, 0, content.Length, CancellationToken.None));
#else
            return stream.WriteAsync(content.AsMemory(), CancellationToken.None);
#endif
        }
    }
}
