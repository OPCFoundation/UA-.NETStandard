/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises detached ticket persistence, restart, publication failures and protected registration.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class FileSystemTicketStoreTests
    {
        /// <summary>
        /// Exact envelopes, metadata and timestamps survive restart without sharing mutable payloads.
        /// </summary>
        [Test]
        public async Task RestartPreservesTicketEnvelopeMetadataAndTimestamp()
        {
            using var files = new VirtualFileSystem();
            var clock = new Mock<TimeProvider>();
            var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(123);
            clock.Setup(value => value.GetUtcNow()).Returns(now);
            byte[] envelope = Encoding.UTF8.GetBytes("{\"payload\":\"unchanged\"}");
            byte[] expected = (byte[])envelope.Clone();
            using (var first = new FileSystemTicketStore(kPath, files, clock.Object))
            {
                Assert.That(await first.GetAsync("one").ConfigureAwait(false), Is.Null);
                Assert.That(await CollectAsync(first).ConfigureAwait(false), Is.Empty);
                await first.AddAsync("one", envelope, Metadata()).ConfigureAwait(false);
                envelope[0] = 0;
            }
            using var restarted = new FileSystemTicketStore(kPath, files);
            TicketRecord? record = await restarted.GetAsync("one").ConfigureAwait(false);
            Assert.That(record, Is.Not.Null);
            Assert.That(record!.TicketId, Is.EqualTo("one"));
            Assert.That(record.EncodedTicket, Is.EqualTo(expected));
            Assert.That(record.Metadata, Is.EqualTo(Metadata()));
            Assert.That(record.CreatedAt, Is.EqualTo(now));
            record.EncodedTicket[0] = 0;
            Assert.That((await restarted.GetAsync("one").ConfigureAwait(false))!.EncodedTicket,
                Is.EqualTo(expected));
            TicketRecord? found = await restarted.FindByProductInstanceUriAsync("urn:device:one").ConfigureAwait(false);
            Assert.That(found!.EncodedTicket, Is.EqualTo(expected));
            Assert.That(await restarted.FindByProductInstanceUriAsync("urn:device:ONE").ConfigureAwait(false), Is.Null);
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// Replacing a key updates its envelope and metadata; removal survives restart and is repeatable.
        /// </summary>
        [Test]
        public async Task ReplacementAndRemovalSurviveRestart()
        {
            using var files = new VirtualFileSystem();
            using (var store = new FileSystemTicketStore(kPath, files))
            {
                await store.AddAsync("one", [1], Metadata()).ConfigureAwait(false);
                await store.AddAsync("one", [2, 3], Metadata() with
                {
                    ProductInstanceUri = "urn:device:replacement",
                    Kind = TicketKind.CompositeIdentity
                }).ConfigureAwait(false);
                Assert.That(await store.FindByProductInstanceUriAsync("urn:device:one").ConfigureAwait(false), Is.Null);
            }
            using (var restarted = new FileSystemTicketStore(kPath, files))
            {
                TicketRecord? record = await restarted.GetAsync("one").ConfigureAwait(false);
                Assert.That(record!.EncodedTicket, Is.EqualTo(new byte[] { 2, 3 }));
                Assert.That(record.Metadata.Kind, Is.EqualTo(TicketKind.CompositeIdentity));
                Assert.That(record.Metadata.ProductInstanceUri, Is.EqualTo("urn:device:replacement"));
                Assert.That(await CollectAsync(restarted).ConfigureAwait(false), Has.Count.EqualTo(1));
                Assert.That(await restarted.RemoveAsync("one").ConfigureAwait(false), Is.True);
                Assert.That(await restarted.RemoveAsync("one").ConfigureAwait(false), Is.False);
            }
            using var empty = new FileSystemTicketStore(kPath, files);
            Assert.That(await CollectAsync(empty).ConfigureAwait(false), Is.Empty);
            Assert.That(await empty.GetAsync("one").ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Ticket keys are ordinal, and empty payloads and undeclared kinds retain the existing store contract.
        /// </summary>
        [Test]
        public async Task OrdinalKeysAndEmptyPayloadsRemainDistinct()
        {
            using var files = new VirtualFileSystem();
            using var store = new FileSystemTicketStore(kPath, files);
            await store.AddAsync("one", [], Metadata() with { Kind = TicketKind.Unspecified }).ConfigureAwait(false);
            await store.AddAsync("One", [2], Metadata()).ConfigureAwait(false);
            TicketRecord? empty = await store.GetAsync("one").ConfigureAwait(false);
            Assert.That(empty!.EncodedTicket, Is.Empty);
            Assert.That(empty.Metadata.Kind, Is.EqualTo(TicketKind.Unspecified));
            Assert.That((await store.GetAsync("One").ConfigureAwait(false))!.EncodedTicket,
                Is.EqualTo(new byte[] { 2 }));
            Assert.That(await store.GetAsync("ONE").ConfigureAwait(false), Is.Null);
            string[] expectedIds = ["One", "one"];
            Assert.That((await CollectAsync(store).ConfigureAwait(false)).Select(record => record.TicketId),
                Is.EqualTo(expectedIds));
        }

        /// <summary>
        /// Concurrent callers retain every accepted update, and readers enumerate detached ordinal snapshots.
        /// </summary>
        [Test]
        public async Task ConcurrentAddsAndSnapshotEnumerationRetainIndependentRecords()
        {
            using var files = new VirtualFileSystem();
            using var store = new FileSystemTicketStore(kPath, files);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task[] additions = Enumerable.Range(0, 20).Select(async index =>
            {
                await start.Task.ConfigureAwait(false);
                await store.AddAsync(index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                    [(byte)index], Metadata()).ConfigureAwait(false);
            }).ToArray();
            start.SetResult(true);
            await Task.WhenAll(additions).ConfigureAwait(false);
            await using IAsyncEnumerator<TicketRecord> snapshot = store.ListAsync().GetAsyncEnumerator();
            Assert.That(await snapshot.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(snapshot.Current.TicketId, Is.EqualTo("00"));
            snapshot.Current.EncodedTicket[0] = 255;
            await store.RemoveAsync("01").ConfigureAwait(false);
            var ids = new List<string> { snapshot.Current.TicketId };
            while (await snapshot.MoveNextAsync().ConfigureAwait(false))
            {
                ids.Add(snapshot.Current.TicketId);
            }
            Assert.That(ids, Is.EqualTo(Enumerable.Range(0, 20)
                .Select(index => index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture))));
            Assert.That((await store.GetAsync("00").ConfigureAwait(false))!.EncodedTicket,
                Is.EqualTo(new byte[] { 0 }));
            Assert.That(await CollectAsync(store).ConfigureAwait(false), Has.Count.EqualTo(19));
        }

        /// <summary>
        /// Corrupt state cannot be concealed by a lookup, a new ticket, or a removal.
        /// </summary>
        [TestCase("version")]
        [TestCase("truncated")]
        [TestCase("trailing")]
        [TestCase("duplicate")]
        [TestCase("kind")]
        [TestCase("timestamp")]
        [TestCase("count")]
        [TestCase("record-limit")]
        [TestCase("empty-id")]
        [TestCase("text-length")]
        [TestCase("invalid-utf8")]
        [TestCase("length")]
        [TestCase("oversized")]
        public async Task CorruptCheckpointRejectsReadsAndWritesWithoutReplacement(string corruption)
        {
            using var files = new VirtualFileSystem();
            byte[] content = Checkpoint(corruption);
            files.Add(kPath, content);
            using var store = new FileSystemTicketStore(kPath, files);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.GetAsync("one").ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.AddAsync("two", [2], Metadata()).ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.RemoveAsync("one").ConfigureAwait(false));
            Assert.That(files.Get(kPath), Is.EqualTo(content));
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// A failed atomic publication preserves the previous checkpoint and reports any additional cleanup failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPublicationPreservesPreviousTicketsAndReportsCleanup(bool cleanupFails)
        {
            using var files = new VirtualFileSystem();
            using (var initial = new FileSystemTicketStore(kPath, files))
            {
                await initial.AddAsync("one", [1], Metadata()).ConfigureAwait(false);
            }
            byte[] before = files.Get(kPath).ToArray();
            Mock<IFileSystem> provider = Forward(files);
            var publication = new IOException("Publication unavailable.");
            var cleanup = new IOException("Staging deletion unavailable.");
            provider.Setup(value => value.Replace(It.IsAny<string>(), kPath)).Throws(publication);
            if (cleanupFails)
            {
                provider.Setup(value => value.Delete(It.IsAny<string>(), false)).Throws(cleanup);
            }
            using var store = new FileSystemTicketStore(kPath, provider.Object);
            if (cleanupFails)
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await store.AddAsync("two", [2], Metadata()).ConfigureAwait(false));
                Assert.That(error!.InnerExceptions, Is.EqualTo(new Exception[] { publication, cleanup }));
                Assert.That(files.Files.Count(), Is.EqualTo(2));
            }
            else
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await store.AddAsync("two", [2], Metadata()).ConfigureAwait(false));
                Assert.That(error, Is.SameAs(publication));
                Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
            }
            Assert.That(files.Get(kPath), Is.EqualTo(before));
            Assert.That(await store.GetAsync("two").ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// An unreadable checkpoint is reported without creating a replacement ticket database.
        /// </summary>
        [Test]
        public async Task ReadFailureDoesNotBecomeAnEmptyStore()
        {
            var files = new Mock<IFileSystem>(MockBehavior.Strict);
            var failure = new IOException("Ticket checkpoint unavailable.");
            files.Setup(value => value.Exists(kPath, false)).Returns(true);
            files.Setup(value => value.OpenRead(kPath)).Throws(failure);
            using var store = new FileSystemTicketStore(kPath, files.Object);
            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await store.GetAsync("one").ConfigureAwait(false));
            Assert.That(error, Is.SameAs(failure));
            error = await Assert.ThrowsAsync<IOException>(async () =>
                await store.AddAsync("two", [2], Metadata()).ConfigureAwait(false));
            Assert.That(error, Is.SameAs(failure));
            files.Verify(value => value.OpenWrite(It.IsAny<string>()), Times.Never);
            files.Verify(value => value.Replace(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// The checkpoint accepts its exact size boundary and rejects the next byte without changing state.
        /// </summary>
        [Test]
        public async Task CheckpointSizeBoundaryPreservesPreviousStateOnOverflow()
        {
            using var files = new VirtualFileSystem();
            using var store = new FileSystemTicketStore(kPath, files);
            int overhead = Checkpoint(string.Empty).Length - 1;
            byte[] payload = new byte[16 * 1024 * 1024 - overhead];
            await store.AddAsync("one", payload, Metadata()).ConfigureAwait(false);
            Assert.That(files.Get(kPath), Has.Length.EqualTo(16 * 1024 * 1024));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.AddAsync("one", new byte[payload.Length + 1], Metadata()).ConfigureAwait(false));
            Assert.That((await store.GetAsync("one").ConfigureAwait(false))!.EncodedTicket,
                Has.Length.EqualTo(payload.Length));
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// The exact record capacity is readable; an additional ticket cannot replace it until one is removed.
        /// </summary>
        [Test]
        public async Task RecordCapacityRejectsOneAdditionalTicketWithoutLosingExistingRecords()
        {
            using var files = new VirtualFileSystem();
            files.Add(kPath, Checkpoint(string.Empty, 10000));
            using var store = new FileSystemTicketStore(kPath, files);
            Assert.That(await CollectAsync(store).ConfigureAwait(false), Has.Count.EqualTo(10000));
            byte[] before = files.Get(kPath).ToArray();
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.AddAsync("overflow", [2], Metadata()).ConfigureAwait(false));
            Assert.That(files.Get(kPath), Is.EqualTo(before));
            Assert.That(await store.GetAsync("overflow").ConfigureAwait(false), Is.Null);
            Assert.That(await store.RemoveAsync("00000").ConfigureAwait(false), Is.True);
            await store.AddAsync("overflow", [2], Metadata()).ConfigureAwait(false);
            Assert.That(await CollectAsync(store).ConfigureAwait(false), Has.Count.EqualTo(10000));
            Assert.That((await store.GetAsync("overflow").ConfigureAwait(false))!.EncodedTicket,
                Is.EqualTo(new byte[] { 2 }));
            Assert.That(files.Files, Is.EquivalentTo(new[] { kPath }));
        }

        /// <summary>
        /// Pre-cancelled and disposed calls never touch storage; cancellation after publication reports success.
        /// </summary>
        [Test]
        public async Task CancellationAndDisposalRespectThePublicationBoundary()
        {
            using var files = new VirtualFileSystem();
            Mock<IFileSystem> provider = Forward(files);
            using var cancellation = new CancellationTokenSource();
            using var store = new FileSystemTicketStore(kPath, provider.Object);
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await store.AddAsync("one", [1], Metadata(), cancellation.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await store.GetAsync("one", cancellation.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await store.RemoveAsync("one", cancellation.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await store.FindByProductInstanceUriAsync("urn:device:one", cancellation.Token).ConfigureAwait(false));
            provider.VerifyNoOtherCalls();
            using var afterPublication = new CancellationTokenSource();
            provider.Setup(value => value.Replace(It.IsAny<string>(), kPath))
                .Callback((string source, string destination) =>
                {
                    files.Replace(source, destination);
                    afterPublication.Cancel();
                });
            await store.AddAsync("one", [1], Metadata(), afterPublication.Token).ConfigureAwait(false);
            Assert.That((await store.GetAsync("one").ConfigureAwait(false))!.EncodedTicket,
                Is.EqualTo(new byte[] { 1 }));
            store.Dispose();
            provider.Invocations.Clear();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.GetAsync("one").ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.AddAsync("one", [1], Metadata()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.RemoveAsync("one").ConfigureAwait(false));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Configuration rejects missing paths and competing stores before mutating registrations.
        /// </summary>
        [Test]
        public async Task FluentRegistrationSharesOneStoreAndRejectsConflicts()
        {
            using var files = new VirtualFileSystem();
            var services = new ServiceCollection();
            Assert.That(() => services.AddFileSystemOnboardingTicketStore(" "),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("path"));
            Assert.That(services, Is.Empty);
            services.AddFileSystemOnboardingTicketStore(kPath, files);
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                ITicketStore contract = provider.GetRequiredService<ITicketStore>();
                Assert.That(contract, Is.SameAs(provider.GetRequiredService<FileSystemTicketStore>()));
                await contract.AddAsync("one", [1], Metadata()).ConfigureAwait(false);
            }
            using var restarted = new FileSystemTicketStore(kPath, files);
            Assert.That((await restarted.GetAsync("one").ConfigureAwait(false))!.EncodedTicket,
                Is.EqualTo(new byte[] { 1 }));
            ServiceDescriptor[] before = services.ToArray();
            Assert.That(() => services.AddFileSystemOnboardingTicketStore(kPath), Throws.InvalidOperationException);
            Assert.That(services.ToArray(), Is.EqualTo(before));
            var custom = new ServiceCollection();
            custom.AddSingleton<ITicketStore>(new MemoryTicketStore());
            before = custom.ToArray();
            Assert.That(() => custom.AddFileSystemOnboardingTicketStore(kPath), Throws.InvalidOperationException);
            Assert.That(custom.ToArray(), Is.EqualTo(before));
        }

        /// <summary>
        /// Invalid public arguments do not create or replace a checkpoint.
        /// </summary>
        [Test]
        public async Task InvalidArgumentsNeverAccessStorage()
        {
            var files = new Mock<IFileSystem>(MockBehavior.Strict);
            Assert.That(() => new FileSystemTicketStore(" "), Throws.ArgumentException);
            using var store = new FileSystemTicketStore(kPath, files.Object);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.AddAsync(string.Empty, [1], Metadata()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.AddAsync("one", null!, Metadata()).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.AddAsync("one", [1], null!).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.AddAsync("one", [1], Metadata() with { Kind = (TicketKind)99 }).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.GetAsync(null!).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.RemoveAsync(null!).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.FindByProductInstanceUriAsync(null!).ConfigureAwait(false));
            files.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Materializes a ticket enumeration without retaining its serialization gate.
        /// </summary>
        private static async Task<List<TicketRecord>> CollectAsync(FileSystemTicketStore store)
        {
            var records = new List<TicketRecord>();
            await foreach (TicketRecord record in store.ListAsync().ConfigureAwait(false))
            {
                records.Add(record);
            }
            return records;
        }

        /// <summary>
        /// Produces a complete record or a specifically malformed checkpoint independently of the store codec.
        /// </summary>
        private static byte[] Checkpoint(string corruption, int count = 1)
        {
            if (corruption == "oversized")
            {
                return new byte[16 * 1024 * 1024 + 1];
            }
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, new UTF8Encoding(false, true), true))
            {
                writer.Write(corruption == "version" ? 2u : 1u);
                writer.Write(corruption switch
                {
                    "count" => -1,
                    "record-limit" => 10001,
                    "duplicate" => 2,
                    _ => count
                });
                for (int i = 0; i < (corruption == "duplicate" ? 2 : count); i++)
                {
                    if (corruption == "text-length")
                    {
                        writer.Write(int.MaxValue);
                    }
                    else if (corruption == "invalid-utf8")
                    {
                        writer.Write(1);
                        writer.Write((byte)0xFF);
                    }
                    else
                    {
                        string id = count == 1 ? "one" :
                            i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
                        WriteText(writer, corruption == "empty-id" ? string.Empty : id);
                    }
                    writer.Write(corruption == "length" ? int.MaxValue : 1);
                    writer.Write((byte)1);
                    TicketMetadata metadata = Metadata();
                    writer.Write(corruption == "kind" ? 99 : (int)metadata.Kind);
                    WriteText(writer, metadata.ManufacturerName);
                    WriteText(writer, metadata.ModelName);
                    WriteText(writer, metadata.SerialNumber);
                    WriteText(writer, metadata.ProductInstanceUri);
                    writer.Write(corruption == "timestamp" ? -1L :
                        new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcDateTime.Ticks);
                }
                if (corruption == "trailing")
                {
                    writer.Write((byte)1);
                }
            }
            byte[] content = buffer.ToArray();
            return corruption == "truncated" ? content.Take(9).ToArray() : content;
        }

        /// <summary>
        /// Writes independent length-prefixed UTF-8 fixture strings.
        /// </summary>
        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(value);
            writer.Write(encoded.Length);
            writer.Write(encoded);
        }

        /// <summary>
        /// Supplies representative persisted metadata independently of certificate or ticket validation.
        /// </summary>
        private static TicketMetadata Metadata()
        {
            return new TicketMetadata(TicketKind.DeviceIdentity, "Vendor", "Model", "Serial", "urn:device:one");
        }

        /// <summary>
        /// Leaves publication and cleanup injectable while forwarding the remaining file operations.
        /// </summary>
        private static Mock<IFileSystem> Forward(VirtualFileSystem files)
        {
            var provider = new Mock<IFileSystem>(MockBehavior.Strict);
            provider.Setup(value => value.Exists(It.IsAny<string>(), false))
                .Returns((string path, bool _) => files.Exists(path));
            provider.Setup(value => value.OpenRead(kPath)).Returns(() => files.OpenRead(kPath));
            provider.Setup(value => value.OpenWrite(It.IsAny<string>()))
                .Returns((string path) => files.OpenWrite(path));
            provider.Setup(value => value.Delete(It.IsAny<string>(), false))
                .Callback((string path, bool _) => files.Delete(path));
            return provider;
        }

        /// <summary>The virtual filesystem checkpoint path.</summary>
        private const string kPath = @"D:\tickets\accepted.bin";
    }
}
