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

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Body admission charges what an HTTPS body actually buffers, bounds what one
    /// peer holds, ends bodies that do not arrive and reclaims capacity when the
    /// reassembly pool refuses a charge.
    /// </summary>
    [TestFixture]
    [Category("HttpBodyAdmission")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HttpBodyAdmissionTests
    {
        private const int kMaxMessageSize = 2 * 1024 * 1024;
        private static readonly IPAddress s_peer = IPAddress.Parse("192.0.2.10");
        private static readonly IPAddress s_otherPeer = IPAddress.Parse("192.0.2.11");

        [Test]
        public async Task AnnouncedLengthDoesNotChargeOrAllocateBeforeBytesArriveAsync()
        {
            var provider = new PoolProvider(long.MaxValue);
            using var admission = new HttpBodyAdmission(provider, provider.Owner, null, s_peer, null);
            var body = new StalledStream();
            var clock = new FakeTimeProvider();

            Task<byte[]> read = ReadAsync(body, kMaxMessageSize, kMaxMessageSize, admission, clock);
            Assert.That(body.ReadStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

            Assert.That(read.IsCompleted, Is.False);
            Assert.That(admission.ChargedBytes, Is.LessThanOrEqualTo(16 * 1024));
            Assert.That(provider.Used, Is.EqualTo(admission.ChargedBytes));
            body.Complete();
            Assert.That(await read.ConfigureAwait(false), Is.Empty);
        }

        [Test]
        public async Task ChargeGrowsWithReceivedBytesAndIsReturnedOnReleaseAsync()
        {
            var provider = new PoolProvider(long.MaxValue);
            using var admission = new HttpBodyAdmission(provider, provider.Owner, null, s_peer, null);
            byte[] payload = new byte[100 * 1024];
            new Random(1).NextBytes(payload);

            byte[] read = await ReadAsync(
                new MemoryStream(payload), payload.Length, kMaxMessageSize, admission, new FakeTimeProvider())
                .ConfigureAwait(false);

            Assert.That(read, Is.EqualTo(payload));
            Assert.That(admission.ChargedBytes, Is.GreaterThanOrEqualTo(payload.Length));
            Assert.That(admission.ChargedBytes, Is.LessThan(3L * payload.Length));
            admission.Release();
            Assert.That(admission.ChargedBytes, Is.Zero);
            Assert.That(provider.Used, Is.Zero);
        }

        [Test]
        public async Task BodyThatDoesNotArriveEndsAtTheDeadlineAsync()
        {
            var provider = new PoolProvider(long.MaxValue);
            using var admission = new HttpBodyAdmission(provider, provider.Owner, null, s_peer, null);
            var clock = new FakeTimeProvider();
            var body = new StalledStream();

            Task<byte[]> read = ReadAsync(body, kMaxMessageSize, kMaxMessageSize, admission, clock);
            Assert.That(body.ReadStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            clock.Advance(TimeSpan.FromSeconds(29));
            Assert.That(read.IsCompleted, Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));

            ServiceResultException? sre = await Assert.ThrowsAsync<ServiceResultException>(() => read).ConfigureAwait(false);
            Assert.That(sre!.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            admission.Release();
            Assert.That(provider.Used, Is.Zero);
        }

        [Test]
        public async Task BodyAboveTheMaximumIsRefusedBeforeReadingAsync()
        {
            var provider = new PoolProvider(long.MaxValue);
            using var admission = new HttpBodyAdmission(provider, provider.Owner, null, s_peer, null);
            var body = new StalledStream();

            ServiceResultException? sre = await Assert.ThrowsAsync<ServiceResultException>(() =>
                ReadAsync(body, 65537, 65536, admission, new FakeTimeProvider())).ConfigureAwait(false);

            Assert.That(sre!.StatusCode, Is.EqualTo(StatusCodes.BadRequestTooLarge));
            Assert.That(body.ReadStarted.IsSet, Is.False);
            Assert.That(admission.ChargedBytes, Is.Zero);
        }

        [Test]
        public async Task BodyWithoutAnnouncedLengthIsBoundedByTheMaximumAsync()
        {
            using var admission = new HttpBodyAdmission(null, null, null, s_peer, null);
            byte[] exact = new byte[40_000];
            new Random(2).NextBytes(exact);

            byte[] read = await ReadAsync(new MemoryStream(exact), null, exact.Length, admission, new FakeTimeProvider())
                .ConfigureAwait(false);
            Assert.That(read, Is.EqualTo(exact));

            ServiceResultException? sre = await Assert.ThrowsAsync<ServiceResultException>(() =>
                ReadAsync(new MemoryStream(exact), null, exact.Length - 1, admission, new FakeTimeProvider()))
                .ConfigureAwait(false);
            Assert.That(sre!.StatusCode, Is.EqualTo(StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public void PeerCeilingBoundsOnePeerWithoutRefusingOthers()
        {
            var ledger = new PeerBodyLedger(100_000);
            using var first = new HttpBodyAdmission(null, null, ledger, s_peer, null);
            using var second = new HttpBodyAdmission(null, null, ledger, s_peer, null);
            using var other = new HttpBodyAdmission(null, null, ledger, s_otherPeer, null);

            Assert.That(first.TryCharge(90_000), Is.True);
            Assert.That(second.TryCharge(20_000), Is.False);
            Assert.That(other.TryCharge(90_000), Is.True);
            Assert.That(ledger.GetCharged(s_peer), Is.EqualTo(90_000));

            first.Release();
            Assert.That(ledger.GetCharged(s_peer), Is.Zero);
            Assert.That(second.TryCharge(20_000), Is.True);
        }

        [Test]
        public void PeerCeilingTreatsMappedAddressesAsOnePeer()
        {
            var ledger = new PeerBodyLedger(100_000);
            using var v4 = new HttpBodyAdmission(null, null, ledger, s_peer, null);
            using var mapped = new HttpBodyAdmission(null, null, ledger, s_peer.MapToIPv6(), null);

            Assert.That(v4.TryCharge(90_000), Is.True);
            Assert.That(mapped.TryCharge(20_000), Is.False);
        }

        [Test]
        public void PoolRefusalReclaimsOnceAndRetries()
        {
            var provider = new PoolProvider(1000);
            using var holder = new HttpBodyAdmission(provider, provider.Owner, null, s_peer, null);
            Assert.That(holder.TryCharge(1000), Is.True);
            int reclaims = 0;
            using var admission = new HttpBodyAdmission(provider, provider.Owner, null, s_otherPeer, () =>
            {
                reclaims++;
                holder.Release();
                return true;
            });

            Assert.That(admission.TryCharge(500), Is.True);

            Assert.That(reclaims, Is.EqualTo(1));
            Assert.That(provider.Used, Is.EqualTo(500));
        }

        [Test]
        public void PeerCeilingRefusalDoesNotReclaimOtherPeersChannels()
        {
            var ledger = new PeerBodyLedger(1000);
            int reclaims = 0;
            using var admission = new HttpBodyAdmission(null, null, ledger, s_peer, () =>
            {
                reclaims++;
                return true;
            });

            Assert.That(admission.TryCharge(2000), Is.False);
            Assert.That(reclaims, Is.Zero);
        }

        [Test]
        public void RefusedChargeLeavesNothingCharged()
        {
            var provider = new PoolProvider(1000);
            var ledger = new PeerBodyLedger(10_000);
            using var admission = new HttpBodyAdmission(provider, provider.Owner, ledger, s_peer, null);

            Assert.That(admission.TryCharge(2000), Is.False);

            Assert.That(admission.ChargedBytes, Is.Zero);
            Assert.That(provider.Used, Is.Zero);
            Assert.That(ledger.GetCharged(s_peer), Is.Zero);
        }

        /// <summary>
        /// Reads with a 30 s deadline whose expiry cancels the stream read, as aborting
        /// the connection does.
        /// </summary>
        private static async Task<byte[]> ReadAsync(
            Stream body,
            long? contentLength,
            int maxLength,
            HttpBodyAdmission admission,
            TimeProvider clock)
        {
            var aborted = new CancellationTokenSource();
            return await HttpBodyAdmission.ReadBodyAsync(
                body,
                contentLength,
                maxLength,
                admission,
                TimeSpan.FromSeconds(30),
                clock,
                static state => ((CancellationTokenSource)state!).Cancel(),
                aborted,
                aborted.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// A body stream that delivers nothing until completed.
        /// </summary>
        private sealed class StalledStream : Stream
        {
            public ManualResetEventSlim ReadStarted { get; } = new();

            public void Complete()
            {
                m_completed.TrySetResult(0);
            }

            public override async Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                ReadStarted.Set();
                return await m_completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            private readonly TaskCompletionSource<int> m_completed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// A reassembly pool with a fixed capacity.
        /// </summary>
        private sealed class PoolProvider : IServerResourceIsolationProvider
        {
            public PoolProvider(long capacity)
            {
                m_capacity = capacity;
                Owner = new ResourceIsolationOwner(
                    "peer", ResourceIsolationClass.Established, 1,
                    new long[] { long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue,
                        long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue });
            }

            public ResourceIsolationOwner Owner { get; }

            public long Used => Interlocked.Read(ref m_used);

            public bool UseFairScheduling => false;

            public event Action<ResourceIsolationStage>? CapacityAvailable
            {
                add { }
                remove { }
            }

            public ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint)
            {
                return Owner;
            }

            public ResourceIsolationOwner Classify(
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return Owner;
            }

            public bool IsCurrent(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return true;
            }

            public bool TryAcquire(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                [NotNullWhen(true)] out IDisposable? lease,
                out ResourceIsolationFailure failure)
            {
                failure = default;
                lock (m_lock)
                {
                    if (amount > m_capacity - m_used)
                    {
                        lease = null;
                        return false;
                    }
                    m_used += amount;
                }
                lease = new Lease(this, amount);
                return true;
            }

            private void Return(long amount)
            {
                lock (m_lock)
                {
                    m_used -= amount;
                }
            }

            private sealed class Lease(PoolProvider provider, long amount) : IDisposable
            {
                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                    {
                        provider.Return(amount);
                    }
                }

                private int m_disposed;
            }

            private readonly object m_lock = new();
            private readonly long m_capacity;
            private long m_used;
        }
    }
}
