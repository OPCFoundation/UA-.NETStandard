// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

#if NETFRAMEWORK
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Exercises the legacy asynchronous memory overloads without relying on newer Stream implementations.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class StreamPolyfillsTests
    {
        /// <summary>
        /// A missing target fails before any asynchronous operation or buffer allocation.
        /// </summary>
        [Test]
        public async Task NullStreamIsRejected()
        {
            ArgumentNullException read = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await System.IO.Polyfills.ReadAsync(null!, Memory<byte>.Empty).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            ArgumentNullException write = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await System.IO.Polyfills.WriteAsync(null!, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(read.ParamName, Is.EqualTo("target"));
            Assert.That(write.ParamName, Is.EqualTo("target"));
        }

        /// <summary>
        /// A short read changes only the reported part of the selected destination.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadPreservesSlicesAndShortReadBoundaries(bool nonArrayMemory)
        {
            byte[] destination = [9, 9, 9, 9, 9, 9];
            using var manager = new NonArrayMemory(destination);
            Memory<byte> memory = nonArrayMemory ? manager.Memory : destination.AsMemory();
            using var stream = new MemoryStream([1, 2]);

            int count = await System.IO.Polyfills.ReadAsync(
                stream, memory.Slice(2, 3), CancellationToken.None).ConfigureAwait(false);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(destination, Is.EqualTo(new byte[] { 9, 9, 1, 2, 9, 9 }));
            Assert.That(await System.IO.Polyfills.ReadAsync(
                stream, memory.Slice(2, 3), CancellationToken.None).ConfigureAwait(false), Is.Zero);
            Assert.That(destination, Is.EqualTo(new byte[] { 9, 9, 1, 2, 9, 9 }));
        }

        /// <summary>
        /// Both ownership representations write exactly the selected source, without surrounding bytes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WritePreservesSlices(bool nonArrayMemory)
        {
            byte[] source = [9, 1, 2, 3, 9];
            using var manager = new NonArrayMemory(source);
            ReadOnlyMemory<byte> memory = nonArrayMemory ? manager.Memory : source.AsMemory();
            using var stream = new MemoryStream();

            await System.IO.Polyfills.WriteAsync(stream, memory.Slice(1, 3), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(source, Is.EqualTo(new byte[] { 9, 1, 2, 3, 9 }));
        }

        /// <summary>
        /// Cancellation reaches the underlying operation without copying or committing user data.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationDoesNotReadOrWrite(bool nonArrayMemory)
        {
            byte[] bytes = [7, 7];
            using var manager = new NonArrayMemory(bytes);
            Memory<byte> memory = nonArrayMemory ? manager.Memory : bytes.AsMemory();
            using var stream = new MemoryStream([1, 2]);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            OperationCanceledException read = (await Assert.CatchAsync<OperationCanceledException>(async () =>
                await System.IO.Polyfills.ReadAsync(stream, memory, cancelled.Token).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            OperationCanceledException write = (await Assert.CatchAsync<OperationCanceledException>(async () =>
                await System.IO.Polyfills.WriteAsync(stream, memory, cancelled.Token).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(read.CancellationToken, Is.EqualTo(cancelled.Token));
                Assert.That(write.CancellationToken, Is.EqualTo(cancelled.Token));
                Assert.That(bytes, Is.EqualTo(new byte[] { 7, 7 }));
                Assert.That(stream.Position, Is.Zero);
                Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
            });
        }

        /// <summary>
        /// Empty memory performs a zero-length operation without changing stream position or content.
        /// </summary>
        [Test]
        public async Task EmptyMemoryDoesNotConsumeOrAppend()
        {
            using var stream = new MemoryStream([1, 2]);
            int count = await System.IO.Polyfills.ReadAsync(stream, Memory<byte>.Empty).ConfigureAwait(false);
            await System.IO.Polyfills.WriteAsync(stream, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);

            Assert.That(count, Is.Zero);
            Assert.That(stream.Position, Is.Zero);
            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
        }

        /// <summary>
        /// Native stream failures retain their original type and are not converted into success.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposedStreamErrorsPropagate(bool nonArrayMemory)
        {
            byte[] bytes = [7, 7];
            using var manager = new NonArrayMemory(bytes);
            Memory<byte> memory = nonArrayMemory ? manager.Memory : bytes.AsMemory();
            using var stream = new MemoryStream([1, 2]);
            stream.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await System.IO.Polyfills.ReadAsync(stream, memory).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await System.IO.Polyfills.WriteAsync(stream, memory).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(bytes, Is.EqualTo(new byte[] { 7, 7 }));
        }

        /// <summary>
        /// Supplies memory that deliberately cannot expose an array segment.
        /// </summary>
        private sealed class NonArrayMemory(byte[] bytes) : MemoryManager<byte>
        {
            /// <inheritdoc/>
            public override Span<byte> GetSpan()
            {
                return bytes;
            }

            /// <inheritdoc/>
            public override MemoryHandle Pin(int elementIndex = 0)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public override void Unpin()
            {
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
            }
        }
    }
}
#endif
