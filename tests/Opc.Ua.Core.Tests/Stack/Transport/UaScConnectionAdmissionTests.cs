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

#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Parallelizable]
    public sealed class UaScConnectionAdmissionTests
    {
        [Test]
        public async Task SimultaneousAdmissionsCannotExceedCapacityAsync()
        {
            var admission = new UaScConnectionAdmission(1, null);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<UaScConnectionAdmission.Lease?>[] attempts = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease);
                    return lease;
                })).ToArray();
            start.SetResult(true);
            UaScConnectionAdmission.Lease?[] leases = await Task.WhenAll(attempts)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            try
            {
                Assert.That(leases.Count(value => value != null), Is.EqualTo(1));
                UaScConnectionAdmission.Lease winner = leases.Single(value => value != null)!;
                winner.Dispose();
                winner.Dispose();
                Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? recovered), Is.True);
                using (recovered)
                {
                    Assert.That(admission.TryAcquire(null, out _), Is.False);
                }
            }
            finally
            {
                admission.Stop();
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveCapacityIsUnlimited(int maximum)
        {
            var admission = new UaScConnectionAdmission(maximum, null);
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    Assert.That(admission.TryAcquire(null, out _), Is.True);
                }
            }
            finally
            {
                admission.Stop();
            }
        }

        [Test]
        public void RejectedLimiterDoesNotReserveOrTakeOwnership()
        {
            var limiter = new SwitchableLimiter { Allow = false };
            var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
            var admission = new UaScConnectionAdmission(1, limiter);
            Assert.That(admission.TryAcquire(endpoint, out UaScConnectionAdmission.Lease? rejected), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(limiter.LastEndpoint, Is.EqualTo(endpoint));
            limiter.Allow = true;
            Assert.That(admission.TryAcquire(endpoint, out UaScConnectionAdmission.Lease? lease), Is.True);
            using (lease)
            {
                Assert.That(limiter.Calls, Is.EqualTo(2));
            }
            admission.Stop();
            Assert.That(limiter.Disposed, Is.False);
        }

        [Test]
        public void StopDuringLimiterCallbackRejectsAdmission()
        {
            var limiter = new SwitchableLimiter();
            var admission = new UaScConnectionAdmission(1, limiter);
            limiter.OnAdmit = admission.Stop;
            Assert.That(admission.TryAcquire(null, out _), Is.False);
        }

        [Test]
        public async Task PhysicalCloseReleasesCapacityExactlyOnceAsync()
        {
            var admission = new UaScConnectionAdmission(1, null);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            var transport = new Mock<IUaSCByteTransport>();
            lease!.Attach(transport.Object);
            Task closed = lease.WaitForCloseAsync(CancellationToken.None);
            Assert.That(closed.IsCompleted, Is.False);
            Assert.That(admission.TryAcquire(null, out _), Is.False);
            lease.Close();
            await closed.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            lease.Dispose();
            transport.Verify(value => value.Close(), Times.Once);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? recovered), Is.True);
            using (recovered)
            {
                Assert.That(admission.TryAcquire(null, out _), Is.False);
            }
        }

        [Test]
        public async Task CapacityIsHeldWhilePhysicalCloseIsInProgressAsync()
        {
            var admission = new UaScConnectionAdmission(1, null);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new Mock<IUaSCByteTransport>();
            transport.Setup(value => value.Close()).Callback(() =>
            {
                entered.TrySetResult(true);
                Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True);
            });
            lease!.Attach(transport.Object);
            Task closing = Task.Run(lease.Close);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(admission.TryAcquire(null, out _), Is.False);
                Assert.That(lease.WaitForCloseAsync(CancellationToken.None).IsCompleted, Is.False);
            }
            finally
            {
                release.Set();
                await closing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? recovered), Is.True);
            recovered!.Dispose();
        }

        [Test]
        public async Task CancellationDoesNotReleaseUntilPhysicalCloseAsync()
        {
            var admission = new UaScConnectionAdmission(1, null);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            using (lease)
            using (var cancellation = new CancellationTokenSource())
            {
                var transport = new Mock<IUaSCByteTransport>();
                lease!.Attach(transport.Object);
                Task closed = lease.WaitForCloseAsync(cancellation.Token);
                cancellation.Cancel();
                await closed.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(admission.TryAcquire(null, out _), Is.False);
                transport.Verify(value => value.Close(), Times.Never);
            }
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? recovered), Is.True);
            recovered!.Dispose();
        }

        [Test]
        public async Task StopClosesTransportAndRejectsLateAttachmentAsync()
        {
            var admission = new UaScConnectionAdmission(2, null);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? attached), Is.True);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? pending), Is.True);
            var transport = new Mock<IUaSCByteTransport>();
            attached!.Attach(transport.Object);
            admission.Stop();
            await attached.WaitForCloseAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var lateTransport = new Mock<IUaSCByteTransport>();
            Assert.Throws<ObjectDisposedException>(() => pending!.Attach(lateTransport.Object));
            attached.Dispose();
            pending!.Dispose();
            admission.Stop();
            transport.Verify(value => value.Close(), Times.Once);
            lateTransport.Verify(value => value.Close(), Times.Once);
            Assert.That(admission.TryAcquire(null, out _), Is.False);
        }

        [Test]
        public void TransportLimitsSurviveLeaseWrapping()
        {
            var admission = new UaScConnectionAdmission(1, null);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            using (lease)
            {
                var transport = new LimitedTransport();
                lease!.Attach(transport);
                ((IUaSCByteTransportLimits)lease).SetReceiveBufferSize(8192);
                Assert.That(transport.ReceiveBufferSize, Is.EqualTo(8192));
            }
        }

        [Test]
        public void RestartRetainsConfiguredAdmissionSettings()
        {
            var limiter = new SwitchableLimiter();
            var admission = new UaScConnectionAdmission(1, limiter);
            admission.Stop();
            Assert.That(admission.TryAcquire(null, out _), Is.False);
            Assert.That(limiter.Calls, Is.Zero);
            admission.Start();
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            using (lease)
            {
                Assert.That(admission.TryAcquire(null, out _), Is.False);
            }
            limiter.Allow = false;
            Assert.That(admission.TryAcquire(null, out _), Is.False);
            Assert.That(limiter.Calls, Is.EqualTo(3));
            Assert.That(limiter.Disposed, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConnectionIsAbortedExactlyOnceWithOrWithoutAttachedTransport(bool attachTransport)
        {
            var admission = new UaScConnectionAdmission(1, null);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            int aborts = 0;
            lease!.SetAbortAction(() => aborts++);
            if (attachTransport)
            {
                var transport = new Mock<IUaSCByteTransport>();
                transport.Setup(value => value.Close()).Callback(() => aborts++);
                lease.Attach(transport.Object);
            }
            lease.Close();
            lease.Dispose();
            admission.Stop();
            Assert.That(aborts, Is.EqualTo(1));
        }

        private sealed class LimitedTransport : IUaSCByteTransport, IUaSCByteTransportLimits
        {
            public int ReceiveBufferSize { get; private set; }
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public string Implementation => "test";

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void Close()
            {
            }

            public void SetReceiveBufferSize(int receiveBufferSize)
            {
                ReceiveBufferSize = receiveBufferSize;
            }
        }

        internal sealed class SwitchableLimiter : IConnectionRateLimiter
        {
            public bool Allow
            {
                get => Volatile.Read(ref m_allow);
                set => Volatile.Write(ref m_allow, value);
            }
            public int Calls => Volatile.Read(ref m_calls);
            public bool Disposed { get; private set; }
            public EndPoint? LastEndpoint { get; private set; }
            public Action? OnAdmit { get; set; }

            public bool TryAdmitConnection(EndPoint? remoteEndPoint, out TimeSpan? retryAfter)
            {
                Interlocked.Increment(ref m_calls);
                LastEndpoint = remoteEndPoint;
                OnAdmit?.Invoke();
                retryAfter = null;
                return Allow;
            }

            public void Dispose()
            {
                Disposed = true;
            }

            private int m_calls;
            private bool m_allow = true;
        }
    }
}
