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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Client;
using Opc.Ua.AutoID.Server;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Tests;

namespace Opc.Ua.AutoID.Tests
{
    public sealed partial class AutoIdReaderEndToEndTests
    {
        /// <summary>
        /// Each supported kind exposes read-only FileType images and preserves open reader snapshots.
        /// </summary>
        [TestCase(AutoIdReaderKind.Ocr)]
        [TestCase(AutoIdReaderKind.Optical)]
        [TestCase(AutoIdReaderKind.OpticalVerifier)]
        public async Task ImageSlotsPublishReadOnlySnapshotsThroughStandardFilesAsync(AutoIdReaderKind kind)
        {
            AutoIdHostFixture host = m_host ?? throw new InvalidOperationException("The host is not initialized.");
            var provider = new MinimalProvider(kind);
            byte[] initial = Encoding.ASCII.GetBytes("initial");
            var name = new QualifiedName("Frame", host.Manager.InstanceNamespaceIndex);
            AutoIdImageSlot[] slots = [new(name, "image/png", ByteString.From(initial), maximumBytes: 7)];
            var builder = host.Runtime.Reader(new QualifiedName("Images" + kind, name.NamespaceIndex), provider)
                .WithImages(slots.ToArrayOf());
            initial[0] = 0;
            slots[0] = new AutoIdImageSlot(name, "image/jpeg");
            await using AutoIdReader reader = await builder.BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client!.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            var files = new List<UaFileInfo>();
            await foreach (UaFileInfo file in m_client.EnumerateImagesAsync(entry))
            {
                files.Add(file);
            }
            Assert.That(files, Has.Count.EqualTo(1));
            UaFileInfo image = files[0];
            Assert.Multiple(() =>
            {
                Assert.That(image.MimeType, Is.EqualTo("image/png"));
                Assert.That(image.Size, Is.EqualTo(7));
                Assert.That(image.Writable, Is.False);
                Assert.That(image.UserWritable, Is.False);
                Assert.That(image.IsDetached, Is.True);
            });
            await using UaFileStream old = await image.OpenReadAsync().ConfigureAwait(false);
            byte[] published = Encoding.ASCII.GetBytes("updated");
            await ((IAutoIdImageSink)provider.Sink!).ReportImageAsync(name, ByteString.From(published))
                .ConfigureAwait(false);
            published[0] = 0;
            byte[] oldBytes = new byte[7];
#if NET
            int read = await old.ReadAsync(oldBytes.AsMemory(), CancellationToken.None).ConfigureAwait(false);
#else
            int read = await old.ReadAsync(oldBytes, 0, oldBytes.Length, CancellationToken.None).ConfigureAwait(false);
#endif
            byte[] latest = await image.ReadAllBytesAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(read, Is.EqualTo(7));
                Assert.That(Encoding.ASCII.GetString(oldBytes), Is.EqualTo("initial"));
                Assert.That(Encoding.ASCII.GetString(latest), Is.EqualTo("updated"));
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                async () => await image.OpenWriteAsync().ConfigureAwait(false));
            await using ManagedSession other = await host.ConnectAsync("ImageIsolation" + kind).ConfigureAwait(false);
            var proxy = new FileTypeClient(other, image.NodeId, NUnitTelemetryContext.Create());
            ServiceResultException foreign = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await proxy.ReadAsync(old.Handle, 1, CancellationToken.None).ConfigureAwait(false)))!;
            Assert.That(foreign.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        /// <summary>
        /// Rejected provider publications do not replace the last valid image.
        /// </summary>
        [Test]
        public async Task ImagePublicationRejectsUnknownNullOversizedAndCanceledUpdatesAsync()
        {
            AutoIdHostFixture host = m_host ?? throw new InvalidOperationException("The host is not initialized.");
            using var provider = new InMemoryScanReaderProvider(AutoIdReaderKind.Ocr);
            var name = new QualifiedName("Frame", host.Manager.InstanceNamespaceIndex);
            await using AutoIdReader reader = await host.Runtime
                .Reader(new QualifiedName("ImageLimits", name.NamespaceIndex), provider)
                .WithImages([new AutoIdImageSlot(name, "image/png", maximumBytes: 3)])
                .BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client!.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            var files = new List<UaFileInfo>();
            await foreach (UaFileInfo image in m_client.EnumerateImagesAsync(entry))
            {
                files.Add(image);
            }
            UaFileInfo file = files.Single();
            await Assert.ThrowsAsync<System.IO.FileNotFoundException>(
                async () => await file.ReadAllBytesAsync().ConfigureAwait(false));
            await reader.ReportImageAsync(name, ByteString.Empty).ConfigureAwait(false);
            Assert.That(await file.ReadAllBytesAsync().ConfigureAwait(false), Is.Empty);
            await reader.ReportImageAsync(name, ByteString.From(Encoding.ASCII.GetBytes("abc"))).ConfigureAwait(false);
            ServiceResultException unknown = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await reader.ReportImageAsync(new QualifiedName("Unknown"), ByteString.Empty)
                    .ConfigureAwait(false)))!;
            Assert.That(unknown.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await reader.ReportImageAsync(name, default).ConfigureAwait(false));
            ServiceResultException oversized = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await reader.ReportImageAsync(name, ByteString.From(Encoding.ASCII.GetBytes("abcd")))
                    .ConfigureAwait(false)))!;
            Assert.That(oversized.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await reader.ReportImageAsync(name, ByteString.Empty, canceled.Token)
                    .ConfigureAwait(false));
            Assert.That(
                Encoding.ASCII.GetString(await file.ReadAllBytesAsync().ConfigureAwait(false)), Is.EqualTo("abc"));
        }

        /// <summary>
        /// Reader disposal retires its file handles and rejects subsequent publications.
        /// </summary>
        [Test]
        public async Task DisposingAReaderRetiresImageBindingsAsync()
        {
            AutoIdHostFixture host = m_host ?? throw new InvalidOperationException("The host is not initialized.");
            using var provider = new InMemoryScanReaderProvider(AutoIdReaderKind.Optical);
            var name = new QualifiedName("Frame", host.Manager.InstanceNamespaceIndex);
            await using AutoIdReader reader = await host.Runtime
                .Reader(new QualifiedName("ImageDisposal", name.NamespaceIndex), provider)
                .WithImages([new AutoIdImageSlot(name, "image/png", ByteString.Empty)])
                .BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client!.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            var images = new List<UaFileInfo>();
            await foreach (UaFileInfo image in m_client.EnumerateImagesAsync(entry))
            {
                images.Add(image);
            }
            var proxy = new FileTypeClient(m_session!, images.Single().NodeId, NUnitTelemetryContext.Create());
            uint handle = await proxy.OpenAsync((byte)UaFileMode.Read, CancellationToken.None).ConfigureAwait(false);
            await reader.DisposeAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await reader.ReportImageAsync(name, ByteString.Empty).ConfigureAwait(false));
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await proxy.ReadAsync(handle, 1, CancellationToken.None).ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Shutdown waits for accepted image publication notifications before retiring the binding.
        /// </summary>
        [Test]
        public async Task ImagePublicationKeepsReaderLifetimeUntilNotificationsCompleteAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            AutoIdReader reader = await fixture.Runtime.Reader(
                fixture.Name("ImageLifetime"), new MinimalProvider(AutoIdReaderKind.Optical))
                .WithImages([new AutoIdImageSlot(fixture.Name("Frame"), "image/png", ByteString.Empty)])
                .BuildAsync().ConfigureAwait(false);
            var optical = (OpticalReaderDeviceState)reader.Device;
            var file = (FileState)optical.Images!.FindChild(fixture.Context, fixture.Name("Frame"))!;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            file.Size!.OnStateChangedAsync = async (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            };
            await AutoIdGatedTest.RunAsync(
                reader,
                async () =>
                {
                    await reader.ReportImageAsync(fixture.Name("Frame"), ByteString.From(1)).ConfigureAwait(false);
                    return true;
                },
                () => release.TrySetResult(true),
                async (invocation, startDisposal) =>
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task<bool> report = await invocation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task dispose = await startDisposal().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(report.IsCompleted, Is.False);
                        Assert.That(dispose.IsCompleted, Is.False);
                    });
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Image names, sizes, counts and reader kinds are validated before registration.
        /// </summary>
        [Test]
        public void ImageConfigurationRejectsInvalidSlots()
        {
            AutoIdHostFixture host = m_host ?? throw new InvalidOperationException("The host is not initialized.");
            var name = new QualifiedName("Frame", host.Manager.InstanceNamespaceIndex);
            Assert.Throws<ArgumentException>(() => new AutoIdImageSlot(default, "image/png"));
            Assert.Throws<ArgumentException>(() => new AutoIdImageSlot(name, string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AutoIdImageSlot(name, "image/png", maximumBytes: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AutoIdImageSlot(
                name, "image/png", maximumBytes: 16 * 1024 * 1024 + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AutoIdImageSlot(
                name, "image/png", ByteString.From(Encoding.ASCII.GetBytes("abcd")), maximumBytes: 3));
            using var provider = new InMemoryScanReaderProvider(AutoIdReaderKind.Optical);
            var builder = host.Runtime.Reader(new QualifiedName("InvalidImages", name.NamespaceIndex), provider);
            var slot = new AutoIdImageSlot(name, "image/png");
            Assert.Throws<ArgumentException>(() => builder.WithImages([slot, slot]));
            Assert.Throws<ArgumentException>(() => builder.WithImages([null!]));
            AutoIdImageSlot[] tooMany = Enumerable.Range(0, 17)
                .Select(i => new AutoIdImageSlot(new QualifiedName("Frame" + i), "image/png")).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithImages(tooMany.ToArrayOf()));
            using var rfid = new InMemoryRfidReaderProvider();
            Assert.Throws<InvalidOperationException>(() => host.Runtime
                .Reader(new QualifiedName("InvalidRfidImages", name.NamespaceIndex), rfid).WithImages([slot]));
        }

        /// <summary>
        /// Unconfigured devices do not advertise an empty Images capability.
        /// </summary>
        [Test]
        public async Task UnconfiguredReaderImagesRemainAbsentAsync()
        {
            AutoIdReaderEntry entry = await ReaderAsync(AutoIdHostFixture.OpticalName).ConfigureAwait(false);
            int count = 0;
            await foreach (UaFileInfo _ in m_client!.EnumerateImagesAsync(entry))
            {
                count++;
            }
            Assert.That(count, Is.Zero);
        }

        /// <summary>
        /// Invalid client arguments are rejected before creating the asynchronous iterator.
        /// </summary>
        [Test]
        public void ImageEnumerationRejectsNullReader()
        {
            Assert.Throws<ArgumentNullException>(() => m_client!.EnumerateImagesAsync(null!));
        }

        /// <summary>
        /// IOData retains the application's sensor/actuator nodes, access levels and asynchronous bindings.
        /// </summary>
        [TestCase(AutoIdReaderKind.Rfid)]
        [TestCase(AutoIdReaderKind.Ocr)]
        [TestCase(AutoIdReaderKind.Optical)]
        [TestCase(AutoIdReaderKind.OpticalVerifier)]
        [TestCase(AutoIdReaderKind.Rtls)]
        public async Task IODataCompositionPreservesApplicationBindingsAsync(AutoIdReaderKind kind)
        {
            AutoIdHostFixture host = m_host ?? throw new InvalidOperationException("The host is not initialized.");
            BaseDataVariableState? trigger = null;
            int writes = 0;
            IAutoIdReaderProvider provider = kind == AutoIdReaderKind.Rtls
                ? new FakeRtlsProvider(kind, [LocationTypeEnumeration.LOCAL])
                : new MinimalProvider(kind);
            await using AutoIdReader reader = await host.Runtime
                .Reader(new QualifiedName("IOData" + kind, host.Manager.InstanceNamespaceIndex),
                    provider)
                .WithIOData((context, group) =>
                {
                    trigger = new BaseDataVariableState(group);
                    trigger.Create(context, NodeId.Null,
                        new QualifiedName("Trigger", host.Manager.InstanceNamespaceIndex),
                        new LocalizedText("Trigger"), true);
                    trigger.DataType = Types.DataTypeIds.Boolean;
                    trigger.ValueRank = ValueRanks.Scalar;
                    trigger.AccessLevel = trigger.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                    trigger.Value = Variant.From(false);
                    trigger.OnWriteValueAsync = (_, _, _, _, _) =>
                    {
                        writes++;
                        return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(ServiceResult.Good));
                    };
                    group.AddChild(trigger);
                }).BuildAsync().ConfigureAwait(false);
            Assert.That(reader.Device.IOData, Is.Not.Null);
            BaseDataVariableState variable = trigger ??
                throw new InvalidOperationException("IOData was not configured.");
            Assert.That(host.Manager.FindPredefinedNode(variable.NodeId), Is.SameAs(variable));
            Assert.That(reader.Device.IOData!.BrowseName,
                Is.EqualTo(new QualifiedName("IOData", host.Manager.SystemContext.NamespaceUris
                    .GetIndexOrAppend(AutoIdModel.NamespaceUri))));
            WriteResponse response = await m_session!.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = variable.NodeId, AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(true))
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results[0], Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(writes, Is.EqualTo(1));
                Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentReadOrWrite));
                Assert.That(variable.BrowseName.NamespaceIndex, Is.EqualTo(host.Manager.InstanceNamespaceIndex));
            });
        }

        /// <summary>
        /// IOData is absent unless configured, and callback failure leaves the reader name available for retry.
        /// </summary>
        [Test]
        public async Task IODataConfigurationFailureRollsBackAndUnconfiguredGroupRemainsAbsentAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var builder = fixture.Runtime.Reader(
                fixture.Name("IODataRetry"), new MinimalProvider(AutoIdReaderKind.Ocr));
            Assert.Throws<ArgumentNullException>(() => builder.WithIOData(null!));
            builder.WithIOData((_, _) => throw new InvalidOperationException("Injected IOData setup failure."));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await builder.BuildAsync()
                .ConfigureAwait(false));
            Assert.That(fixture.Runtime.Readers, Has.Count.EqualTo(0));
            await using AutoIdReader retried = await fixture.Runtime
                .Reader(fixture.Name("IODataRetry"), new MinimalProvider(AutoIdReaderKind.Ocr))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(retried.Device.IOData, Is.Null);
        }

        /// <summary>
        /// The fixed slot-count boundary is accepted and every file is independently named.
        /// </summary>
        [Test]
        public async Task SixteenImageSlotsAreSupportedAsync()
        {
            AutoIdHostFixture host = m_host ?? throw new InvalidOperationException("The host is not initialized.");
            var provider = new MinimalProvider(AutoIdReaderKind.Ocr);
            AutoIdImageSlot[] slots = Enumerable.Range(0, 16)
                .Select(i => new AutoIdImageSlot(new QualifiedName(
                    "Frame" + i, host.Manager.InstanceNamespaceIndex), "image/png", ByteString.Empty))
                .ToArray();
            await using AutoIdReader reader = await host.Runtime
                .Reader(new QualifiedName("ImageCapacity", host.Manager.InstanceNamespaceIndex), provider)
                .WithImages(slots.ToArrayOf()).BuildAsync().ConfigureAwait(false);
            AutoIdReaderEntry entry = await m_client!.GetReaderAsync(reader.NodeId).ConfigureAwait(false);
            var names = new HashSet<QualifiedName>();
            await foreach (UaFileInfo image in m_client.EnumerateImagesAsync(entry))
            {
                names.Add(image.BrowseName);
            }
            Assert.That(names, Is.EquivalentTo(slots.Select(slot => slot.Name)));
        }

        /// <summary>
        /// Partial binding failure disposes earlier bindings and rolls back registration so the name can be retried.
        /// </summary>
        [Test]
        public async Task FailedImageBindingDisposesEarlierSlotsAndAllowsRegistrationRetryAsync()
        {
            var binder = new FailingImageBinder();
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync(fileBinder: binder).ConfigureAwait(false);
            var provider = new MinimalProvider(AutoIdReaderKind.Ocr);
            var builder = fixture.Runtime.Reader(fixture.Name("Camera"), provider)
                .WithImages(
                [
                    new AutoIdImageSlot(fixture.Name("First"), "image/png", ByteString.Empty),
                    new AutoIdImageSlot(fixture.Name("Second"), "image/png", ByteString.Empty)
                ]);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await builder.BuildAsync()
                .ConfigureAwait(false));
            Assert.That(fixture.Runtime.Readers, Has.Count.EqualTo(0));
            Assert.That(provider.Sink, Is.Null, "failed image setup must not attach the provider");
            IFileTypeBinding first = binder.First ?? throw new InvalidOperationException("The binder was not called.");
            ServiceResultException disposed = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await first.OpenAsync(NodeId.Null, (byte)UaFileMode.Read).ConfigureAwait(false)))!;
            Assert.That(disposed.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            await using AutoIdReader retried = await fixture.Runtime.Reader(fixture.Name("Camera"), provider)
                .WithImages(
                [
                    new AutoIdImageSlot(fixture.Name("First"), "image/png", ByteString.Empty),
                    new AutoIdImageSlot(fixture.Name("Second"), "image/png", ByteString.Empty)
                ]).BuildAsync().ConfigureAwait(false);
            Assert.That(retried.Name, Is.EqualTo(fixture.Name("Camera")));
            await retried.ReportImageAsync(fixture.Name("First"), ByteString.Empty).ConfigureAwait(false);
        }

        /// <summary>
        /// Uses the standard binder except for an injected second-slot failure.
        /// </summary>
        private sealed class FailingImageBinder : IFileTypeBinder
        {
            /// <summary>
            /// Gets the first binding, which the reader must dispose after the second one fails.
            /// </summary>
            public IFileTypeBinding? First { get; private set; }

            /// <inheritdoc/>
            public IFileTypeBinding Bind(
                FileState file, ISystemContext context, ByteString content,
                Func<ByteString, CancellationToken, ValueTask>? commit = null,
                bool commitOnClose = false, NodeId ownerSession = default, int maximumBytes = 16 * 1024 * 1024)
            {
                m_calls++;
                if (m_calls == 2)
                {
                    throw new InvalidOperationException("Injected second-image failure.");
                }
                IFileTypeBinding binding = new FileTypeBinder().Bind(
                    file, context, content, commit, commitOnClose, ownerSession, maximumBytes);
                First ??= binding;
                return binding;
            }

            /// <summary>The number of bindings requested by the runtime.</summary>
            private int m_calls;
        }
    }
}
