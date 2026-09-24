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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    internal sealed class CountingIsolationProvider(
        long connectionLimit = 8,
        long ownerLimit = 8,
        ResourceIsolationStage? rejectedStage = null,
        ResourceIsolationFailureReason rejectedReason = ResourceIsolationFailureReason.Capacity)
        : IServerResourceIsolationProvider
    {
        public bool UseFairScheduling => true;

        public event Action? CapacityAvailable;

        public IPEndPoint? LastEndpoint { get; private set; }

        public ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint)
        {
            LastEndpoint = remoteEndpoint;
            long[] limits = new long[(int)ResourceIsolationStage.ParkedRequest + 1];
            for (int i = 0; i < limits.Length; i++)
            {
                limits[i] = ownerLimit;
            }
            return new ResourceIsolationOwner(
                remoteEndpoint?.Address.ToString() ?? "unknown",
                ResourceIsolationClass.Established, 1, limits);
        }

        public ResourceIsolationOwner Classify(
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false)
        {
            throw new InvalidOperationException("Transport admission must not trust a channel or token claim.");
        }

        public bool IsCurrent(
            ResourceIsolationOwner owner,
            SecureChannelContext channelContext,
            NodeId authenticationToken = default,
            bool sessionEstablishment = false,
            bool controlRequest = false)
        {
            throw new InvalidOperationException("Transport admission cannot verify a session.");
        }

        public bool TryAcquire(
            ResourceIsolationStage stage,
            ResourceIsolationOwner owner,
            long amount,
            [NotNullWhen(true)] out IDisposable? lease,
            out ResourceIsolationFailure failure)
        {
            lock (m_lock)
            {
                lease = null;
                var key = (owner.Key, stage);
                m_owners.TryGetValue(key, out long active);
                if (active + amount > owner.GetHardLimit(stage))
                {
                    failure = new ResourceIsolationFailure(ResourceIsolationFailureReason.OwnerLimit, TimeSpan.Zero);
                    return false;
                }
                if (stage == rejectedStage)
                {
                    failure = new ResourceIsolationFailure(rejectedReason, TimeSpan.Zero);
                    return false;
                }
                if (m_active[(int)stage] + amount > connectionLimit)
                {
                    failure = new ResourceIsolationFailure(ResourceIsolationFailureReason.Capacity, TimeSpan.Zero);
                    return false;
                }
                m_active[(int)stage] += amount;
                m_owners[key] = active + amount;
                lease = new CountedLease(() =>
                {
                    lock (m_lock)
                    {
                        m_active[(int)stage] -= amount;
                        m_owners[key] -= amount;
                    }
                    CapacityAvailable?.Invoke();
                });
                failure = default;
                return true;
            }
        }

        public long Active(ResourceIsolationStage stage)
        {
            lock (m_lock)
            {
                return m_active[(int)stage];
            }
        }

        public async Task WaitForHandshakeCompletionAsync()
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void CheckCompletion()
            {
                if (Active(ResourceIsolationStage.Handshake) == 0)
                {
                    completed.TrySetResult(true);
                }
            }
            CapacityAvailable += CheckCompletion;
            try
            {
                CheckCompletion();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                CapacityAvailable -= CheckCompletion;
            }
        }

        private sealed class CountedLease(Action release) : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            private Action? m_release = release;
        }

        private readonly Lock m_lock = new();
        private readonly long[] m_active = new long[(int)ResourceIsolationStage.ParkedRequest + 1];
        private readonly Dictionary<(string, ResourceIsolationStage), long> m_owners = [];
    }

    [TestFixture]
    [Parallelizable]
    public sealed class UaScConnectionAdmissionTests
    {
        [Test]
        public void HandshakeOwnerRejectionPreventsReclamationEvenWhenConnectionsAreFull()
        {
            var provider = new CountingIsolationProvider(
                connectionLimit: 1,
                ownerLimit: 2,
                rejectedStage: ResourceIsolationStage.Handshake,
                rejectedReason: ResourceIsolationFailureReason.OwnerLimit);
            ResourceIsolationOwner owner = provider.ClassifyConnection(null);
            bool occupied = provider.TryAcquire(
                ResourceIsolationStage.Connection, owner, 1, out IDisposable? existing, out _);
            using (existing)
            {
                Assert.That(occupied, Is.True);
                var limiter = new SwitchableLimiter();
                var admission = new UaScConnectionAdmission(1, limiter, provider);
                int reclaimed = 0;
                Assert.That(admission.TryAcquire(null, out _, () =>
                {
                    reclaimed++;
                    return true;
                }), Is.False);
                Assert.That(reclaimed, Is.Zero);
                Assert.That(limiter.Calls, Is.Zero);
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
            }
        }

        [Test]
        public void CapacityRetryConsumesOneLegacyTokenAndReclaimsAtMostOnce()
        {
            var provider = new CountingIsolationProvider(rejectedStage: ResourceIsolationStage.Connection);
            var limiter = new SwitchableLimiter();
            var admission = new UaScConnectionAdmission(1, limiter, provider);
            int reclaimed = 0;
            Assert.That(admission.TryAcquire(null, out _, () =>
            {
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
                reclaimed++;
                return true;
            }), Is.False);
            Assert.That(reclaimed, Is.EqualTo(1));
            Assert.That(limiter.Calls, Is.EqualTo(1));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SeparateListenersSharePhysicalStageTotals(bool sameAddress)
        {
            var provider = new CountingIsolationProvider(connectionLimit: 2, ownerLimit: 2);
            var first = new UaScConnectionAdmission(10, null, provider);
            var second = new UaScConnectionAdmission(10, null, provider);
            var endpoint = new IPEndPoint(IPAddress.Loopback, 1234);
            var other = new IPEndPoint(sameAddress ? IPAddress.Loopback : IPAddress.IPv6Loopback, 5678);
            Assert.That(first.TryAcquire(endpoint, out UaScConnectionAdmission.Lease? one), Is.True);
            Assert.That(second.TryAcquire(other, out UaScConnectionAdmission.Lease? two), Is.True);
            try
            {
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(2));
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(2));
                Assert.That(second.TryAcquire(other, out _), Is.False);
                one!.CompleteHandshake();
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(2));
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(1));
                Assert.That(first.TryAcquire(endpoint, out _), Is.False);
            }
            finally
            {
                one!.Dispose();
                two!.Dispose();
            }
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [TestCase(ResourceIsolationStage.Connection)]
        [TestCase(ResourceIsolationStage.Handshake)]
        public void IsolationRejectionNeverConsumesLegacyTokens(ResourceIsolationStage rejectedStage)
        {
            var provider = new CountingIsolationProvider(rejectedStage: rejectedStage);
            var limiter = new SwitchableLimiter();
            var admission = new UaScConnectionAdmission(4, limiter, provider);
            for (int i = 0; i < 8; i++)
            {
                Assert.That(admission.TryAcquire(new IPEndPoint(IPAddress.Loopback, 1000 + i), out _), Is.False);
            }
            Assert.That(limiter.Calls, Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [Test]
        public void OwnerLimitSpansPortsAndDoesNotRejectAnotherObservedAddress()
        {
            var provider = new CountingIsolationProvider(connectionLimit: 2, ownerLimit: 1);
            var limiter = new SwitchableLimiter();
            var admission = new UaScConnectionAdmission(4, limiter, provider);
            Assert.That(admission.TryAcquire(
                new IPEndPoint(IPAddress.Loopback, 1), out UaScConnectionAdmission.Lease? first), Is.True);
            using (first)
            {
                Assert.That(admission.TryAcquire(new IPEndPoint(IPAddress.Loopback, 2), out _), Is.False);
                Assert.That(limiter.Calls, Is.EqualTo(1));
                Assert.That(admission.TryAcquire(
                    new IPEndPoint(IPAddress.IPv6Loopback, 1), out UaScConnectionAdmission.Lease? other), Is.True);
                other!.Dispose();
                Assert.That(provider.LastEndpoint!.Address, Is.EqualTo(IPAddress.IPv6Loopback));
                Assert.That(limiter.Calls, Is.EqualTo(2));
            }
        }

        [Test]
        public void LegacyRejectionAndStopDuringAdmissionReturnBothRuntimeLeases()
        {
            var provider = new CountingIsolationProvider();
            var limiter = new SwitchableLimiter { Allow = false };
            var admission = new UaScConnectionAdmission(4, limiter, provider);
            Assert.That(admission.TryAcquire(null, out _), Is.False);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
            limiter.Allow = true;
            limiter.OnAdmit = admission.Stop;
            Assert.That(admission.TryAcquire(null, out _), Is.False);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [Test]
        public void FixedStartupDeadlineClosesSilentAndUnattachedPeers()
        {
            var clock = new FakeTimeProvider();
            var provider = new CountingIsolationProvider();
            var admission = new UaScConnectionAdmission(
                4, null, provider, TimeSpan.FromSeconds(10), clock);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? attached), Is.True);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? pending), Is.True);
            var transport = new Mock<IUaSCByteTransport>();
            attached!.Attach(transport.Object);
            int aborts = 0;
            pending!.SetAbortAction(() => aborts++);
            clock.Advance(TimeSpan.FromSeconds(9));
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(2));
            transport.Verify(value => value.Close(), Times.Never);
            clock.Advance(TimeSpan.FromSeconds(1));
            transport.Verify(value => value.Close(), Times.Once);
            Assert.That(aborts, Is.EqualTo(1));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
            var late = new Mock<IUaSCByteTransport>();
            Assert.Throws<ObjectDisposedException>(() => pending.Attach(late.Object));
            late.Verify(value => value.Close(), Times.Once);
        }

        [Test]
        public void CompletedHandshakeSurvivesStartupDeadlineAndReleasesOnlyHandshake()
        {
            var clock = new FakeTimeProvider();
            var provider = new CountingIsolationProvider();
            var admission = new UaScConnectionAdmission(
                4, null, provider, TimeSpan.FromSeconds(10), clock);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            var transport = new Mock<IUaSCByteTransport>();
            lease!.Attach(transport.Object);
            clock.Advance(TimeSpan.FromSeconds(9));
            ((IUaSCHandshakeCompletionSource)lease).CompleteHandshake();
            lease.CompleteHandshake();
            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
            transport.Verify(value => value.Close(), Times.Never);
            lease.Dispose();
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
        }

        [Test]
        public void DefaultStartupDeadlineIsTwoMinutes()
        {
            var clock = new FakeTimeProvider();
            var provider = new CountingIsolationProvider();
            var admission = new UaScConnectionAdmission(4, null, provider, timeProvider: clock);
            Assert.That(admission.TryAcquire(null, out _), Is.True);
            clock.Advance(TimeSpan.FromSeconds(119));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
        }

        [Test]
        public void AllPendingConnectionsShareOneDeadlineTimer()
        {
            var clock = new TimerCountingClock();
            var admission = new UaScConnectionAdmission(64, null, timeProvider: clock);
            try
            {
                for (int i = 0; i < 64; i++)
                {
                    Assert.That(admission.TryAcquire(null, out _), Is.True);
                }
                Assert.That(clock.CreatedTimers, Is.EqualTo(1));
                clock.Advance(TimeSpan.FromMinutes(2));
                Assert.That(admission.TryAcquire(null, out _), Is.True);
                Assert.That(clock.CreatedTimers, Is.EqualTo(2));
            }
            finally
            {
                admission.Stop();
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(120001)]
        public void InvalidStartupDeadlineIsRejected(int milliseconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new UaScConnectionAdmission(4, null, handshakeTimeout: TimeSpan.FromMilliseconds(milliseconds)));
        }

        [Test]
        public void PhysicalCloseFailureDoesNotAbortTheSameConnectionTwice()
        {
            var provider = new CountingIsolationProvider();
            var admission = new UaScConnectionAdmission(1, null, provider);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? lease), Is.True);
            int aborts = 0;
            lease!.SetAbortAction(() => aborts++);
            var transport = new Mock<IUaSCByteTransport>();
            transport.Setup(value => value.Close()).Callback(() => aborts++)
                .Throws(new InvalidOperationException("physical close failed after abort"));
            lease.Attach(transport.Object);
            Assert.Throws<InvalidOperationException>(lease.Close);
            lease.Dispose();
            Assert.That(aborts, Is.EqualTo(1));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

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

        private sealed class TimerCountingClock : TimeProvider
        {
            public int CreatedTimers { get; private set; }
            public override long TimestampFrequency => m_clock.TimestampFrequency;

            public override long GetTimestamp()
            {
                return m_clock.GetTimestamp();
            }

            public override ITimer CreateTimer(
                TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                CreatedTimers++;
                return m_clock.CreateTimer(callback, state, dueTime, period);
            }

            public void Advance(TimeSpan duration)
            {
                m_clock.Advance(duration);
            }

            private readonly FakeTimeProvider m_clock = new();
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
