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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Proves the bounded result subtree, not the still-independent GMS equipment or production implementation.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [NonParallelizable]
    public sealed class GmsResultIntegrationTests
    {
        /// <summary>
        /// Canonical GMS-qualified result management uses inherited method declarations over an actual TCP session.
        /// </summary>
        [Test]
        public async Task CanonicalGmsRootRetrievesResultsOverTcp()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineBuilder<GMSState> builder = fixture.Stage("GmsResultOnly");
            IMachineHandle<GMSState> machine = await builder
                .WithResultManagement(builder.State.ResultManagement!, results =>
                    results.WithInMemoryStore(2).WithResultsFolder(1,
                        static (context, parent, name) => context.CreateInstanceOfGMSResultType(parent, name)))
                .BuildAsync();
            await machine.Results!.PublishAsync(new MachineryResult(GmsResultStoreTests.Result("tcp-one").Data));
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());

            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("tcp-one"));
            Assert.That((await client.GetByIdAsync("tcp-one")).ResultMetaData!.ResultId, Is.EqualTo("tcp-one"));
            Assert.That((await client.QueryAsync(2)).ToArray(), Is.EqualTo((string[])["tcp-one"]));
            Assert.That(machine.State.ResultManagement!.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                1008, GmsModel.NamespaceUri, session.NamespaceUris)));
            Assert.That(machine.State.ResultManagement.BrowseName, Is.EqualTo(new QualifiedName(
                "ResultManagement", (ushort)session.NamespaceUris.GetIndex(GmsModel.NamespaceUri))));
            Assert.That(session.NamespaceUris.GetIndex(MachineTool.MachineToolModels.WmtpNamespaceUri), Is.EqualTo(-1));
        }

        /// <summary>
        /// Closing the owning session releases its pinned metadata capacity rather than exhausting other sessions.
        /// </summary>
        [Test]
        public async Task ClosingSessionReclaimsOnlyItsMetadataHandles()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineBuilder<GMSState> builder = fixture.Stage("GmsCloseSession");
            IMachineHandle<GMSState> machine = await builder.WithResultManagement(
                builder.State.ResultManagement!, results => results.WithInMemoryStore()).BuildAsync();
            await machine.Results!.PublishAsync(new MachineryResult(GmsResultStoreTests.Result("close-one").Data));
            ISession first = await fixture.ConnectAsync();
            ISession second = await fixture.ConnectAsync();
            var raw = new GMSResultManagementTypeClient(
                first, machine.State.ResultManagement!.NodeId, NUnitTelemetryContext.Create());
            (uint handle, _, _) = await raw.GetLatestResultAsync(0);
            Assert.That(handle, Is.Not.Zero);

            await first.CloseAsync(1000, true);

            var client = new GmsResultClient(second, machine.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("close-one"));
        }

        /// <summary>
        /// A provider completing after its requesting session ends cannot reserve new metadata capacity.
        /// </summary>
        [Test]
        public async Task LateMetadataReadCannotPinAfterItsSessionCloses()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var result = new MachineryResult(GmsResultStoreTests.Result("late-metadata").Data);
            var store = new Mock<IMachineryResultStore>();
            store.Setup(value => value.GetLatestResultAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken _) =>
                {
                    if (Interlocked.Increment(ref reads) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.ConfigureAwait(false);
                    }
                    return result;
                });
            IMachineBuilder<GMSState> builder = fixture.Stage("GmsLateMetadata");
            IMachineHandle<GMSState> machine = await builder.WithResultManagement(
                builder.State.ResultManagement!, results => results.WithStore(store.Object)).BuildAsync();
            ISession first = await fixture.ConnectAsync();
            ISession second = await fixture.ConnectAsync();
            var raw = new GMSResultManagementTypeClient(
                first, machine.State.ResultManagement!.NodeId, NUnitTelemetryContext.Create());
            Task<(uint ResultHandle, Opc.Ua.Machinery.Result.ResultDataType Result, int Error)> pending =
                raw.GetLatestResultAsync(0).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await first.CloseAsync(1000, true);
            }
            finally
            {
                release.TrySetResult(true);
            }
            Exception? failure = await Assert.CatchAsync<Exception>(
                async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(failure, Is.InstanceOf<ServiceResultException>().Or.InstanceOf<OperationCanceledException>());
            var client = new GmsResultClient(second, machine.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("late-metadata"));
        }

        /// <summary>
        /// Closing a session deletes its abandoned transient file and immediately frees the shared transfer capacity.
        /// </summary>
        [Test]
        public async Task ClosingSessionReclaimsItsTransientResultFile()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineBuilder<GMSState> builder = fixture.Stage("GmsCloseTransfer");
            IMachineHandle<GMSState> machine = await builder.WithResultManagement(
                builder.State.ResultManagement!, results => results.WithInMemoryStore().WithFileTransfer()).BuildAsync();
            await machine.Results!.PublishAsync(new MachineryResult(
                GmsResultStoreTests.Result("abandoned-file").Data, new ByteString(new byte[] { 2, 4, 6 })));
            ISession first = await fixture.ConnectAsync();
            ISession second = await fixture.ConnectAsync();
            var transfer = new TemporaryFileTransferTypeClient(
                first, machine.State.ResultManagement!.ResultTransfer!.NodeId, NUnitTelemetryContext.Create());
            (NodeId file, uint handle, _) = await transfer.GenerateFileForReadAsync(
                Variant.FromStructure(new Opc.Ua.Machinery.Result.ResultTransferOptionsDataType
                {
                    ResultId = "abandoned-file"
                }));
            Assert.That(handle, Is.Not.Zero);

            await first.CloseAsync(1000, true);

            ReadResponse removed = await second.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = file, AttributeId = Attributes.NodeClass }], default);
            Assert.That(removed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            var client = new GmsResultClient(second, machine.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await client.DownloadAsync("abandoned-file")).ToArray(), Is.EqualTo(new byte[] { 2, 4, 6 }));
        }

        /// <summary>
        /// The standard temporary-file client bounds bytes and releases file and metadata handles on every exit.
        /// </summary>
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(3, true)]
        public async Task DownloadsAreBoundedAndOwnedResourcesAreReleased(int bytes, bool cancelDuringWrite)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineBuilder<GMSState> builder = fixture.Stage("GmsDownload");
            IMachineHandle<GMSState> machine = await builder.WithResultManagement(
                builder.State.ResultManagement!, results => results.WithInMemoryStore().WithFileTransfer()).BuildAsync();
            await machine.Results!.PublishAsync(new MachineryResult(
                GmsResultStoreTests.Result("file-one").Data, new ByteString(new byte[bytes])));
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create(),
                new GmsResultLimits { MaximumFileBytes = 3 });
            using var cancellation = new CancellationTokenSource();

            if (cancelDuringWrite)
            {
                using var destination = new CancelingDestination(cancellation);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await client.DownloadAsync("file-one", destination, cancellation.Token));
            }
            else if (bytes <= 3)
            {
                Assert.That((await client.DownloadAsync("file-one")).Length, Is.EqualTo(bytes));
            }
            else
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.DownloadAsync("file-one")))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            var unrestricted = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await unrestricted.DownloadAsync("file-one")).Length, Is.EqualTo(bytes));
            Assert.That((await unrestricted.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("file-one"));
        }

        /// <summary>
        /// Typed projection retains each ring entry's quality, timestamp and tool identifiers during rotation.
        /// </summary>
        [Test]
        public async Task GmsBindingPublishesDerivedVariablesWithAcquiredQualityAndTools()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext());
            IMachineHandle<GMSState> machine = await fixture.Stage("GmsProjected")
                .WithGmsResults(store, out GmsResultBinding binding,
                    new GmsResultBindingOptions { PublishedResults = 2, IncludeUsedTools = true })
                .BuildAsync();
            GmsResultPublisher publisher = binding.CreatePublisher(machine);
            var acquired = new DateTimeUtc(2026, 1, 2, 3, 4, 5);
            DataValue first = GmsResultStoreTests.Result("projected-one").Value
                .WithStatus(StatusCodes.UncertainDataSubNormal).WithSourceTimestamp(acquired);
            await publisher.PublishAsync(new GmsResult(first, ["probe-7"]));
            await store.RemoveAsync("projected-one");
            await publisher.PublishAsync(GmsResultStoreTests.Result("projected-two"));
            ISession session = await fixture.ConnectAsync();
            var slots = new List<BaseInstanceState>();
            machine.State.ResultManagement!.Results!.GetChildren(fixture.Manager.SystemContext, slots);
            var previous = (GMSResultState)slots[1];
            DataValue read = await session.ReadValueAsync(previous.NodeId);

            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(read.SourceTimestamp, Is.EqualTo(acquired));
            Assert.That(previous.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                2004, GmsModel.NamespaceUri, session.NamespaceUris)));
            Assert.That(previous.UsedTools!.Value.ToArray(), Is.EqualTo((string[])["probe-7"]));
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await client.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("projected-two"));
        }

        /// <summary>
        /// Cancels at the caller-owned destination boundary after both remote handles have been acquired.
        /// </summary>
        private sealed class CancelingDestination : MemoryStream
        {
            /// <summary>
            /// Captures the caller's cancellation source.
            /// </summary>
            internal CancelingDestination(CancellationTokenSource cancellation)
            {
                m_cancellation = cancellation;
            }

            /// <inheritdoc/>
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                m_cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
            /// <inheritdoc/>
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                m_cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return base.WriteAsync(buffer, cancellationToken);
            }
#endif

            private readonly CancellationTokenSource m_cancellation;
        }
    }
}
