// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Native.QuicGo.Tests
{
    /// <summary>
    /// Exercises the dynamic application-error cancellation contract.
    /// </summary>
    [TestFixture]
    public sealed class NativeQuicCancellationTests
    {
        /// <summary>
        /// Token callbacks see the requested code, including both legal QUIC varint boundaries.
        /// </summary>
        [TestCase(0UL)]
        [TestCase((1UL << 62) - 1)]
        public async Task ErrorCodeIsPublishedBeforeCancellationCallbacksAsync(ulong errorCode)
        {
            using var cancellation = new NativeQuicCancellation();
            Assert.That(cancellation.IsCancellationRequested, Is.False);
            Assert.That(cancellation.Token.IsCancellationRequested, Is.False);
            Assert.That(cancellation.ErrorCode, Is.Zero);
            ulong observed = ulong.MaxValue;
            int calls = 0;
            using CancellationTokenRegistration registration = cancellation.Token.Register(() =>
            {
                observed = cancellation.ErrorCode;
                calls++;
            });
            await cancellation.CancelAsync(errorCode).ConfigureAwait(false);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(cancellation.Token.IsCancellationRequested, Is.True);
            Assert.That(observed, Is.EqualTo(errorCode));
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Repeated signals do not overwrite the code or run callbacks twice.
        /// </summary>
        [Test]
        public async Task FirstRequestedCodeAndSignallingTaskWinAsync()
        {
            using var cancellation = new NativeQuicCancellation();
            int calls = 0;
            using CancellationTokenRegistration registration = cancellation.Token.Register(
                () => Interlocked.Increment(ref calls));
            Task first = cancellation.CancelAsync(91);
            Task second = cancellation.CancelAsync(123);
            Assert.That(second, Is.SameAs(first));
            await Task.WhenAll(first, second).ConfigureAwait(false);
            Assert.That(cancellation.ErrorCode, Is.EqualTo(91));
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid error codes leave the control available for a later valid signal.
        /// </summary>
        [TestCase(1UL << 62)]
        [TestCase(ulong.MaxValue)]
        public async Task InvalidCodeDoesNotPublishCancellationAsync(ulong errorCode)
        {
            using var cancellation = new NativeQuicCancellation();
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await cancellation.CancelAsync(errorCode).ConfigureAwait(false));
            Assert.That(cancellation.IsCancellationRequested, Is.False);
            Assert.That(cancellation.Token.IsCancellationRequested, Is.False);
            Assert.That(cancellation.ErrorCode, Is.Zero);
            await cancellation.CancelAsync(7).ConfigureAwait(false);
            Assert.That(cancellation.ErrorCode, Is.EqualTo(7));
        }

        /// <summary>
        /// Disposal releases resources without an implicit cancellation or a replacement error.
        /// </summary>
        [Test]
        public void DisposalDoesNotCancelOrPermitAnotherRequest()
        {
            var cancellation = new NativeQuicCancellation();
            CancellationToken token = cancellation.Token;
            cancellation.Dispose();
            cancellation.Dispose();
            Assert.That(token.IsCancellationRequested, Is.False);
            Assert.That(cancellation.IsCancellationRequested, Is.False);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await cancellation.CancelAsync(7).ConfigureAwait(false));
        }
    }
}
