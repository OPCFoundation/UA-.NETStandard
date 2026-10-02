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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ReverseConnectionRaceTests
{
    [Test]
    public async Task PreCanceledStopDoesNotCancelAnInFlightStart()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            Task starting = context.BeginStart(cooperative: true);
            await context.StartEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Owner.StopAsync(new CancellationToken(canceled: true)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(context.StartToken.IsCancellationRequested, Is.False);
            Assert.That(starting.IsCompleted, Is.False);
            context.ReleaseStart.TrySetResult();
            await starting.WaitAsync(s_bound).ConfigureAwait(false);
            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task PreCanceledStopDoesNotCancelAnInFlightWait()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);
            Task<ITransportWaitingConnection> waiting = context.BeginWait(cooperative: true);
            await context.WaitEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => context.Owner.StopAsync(new CancellationToken(canceled: true)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(context.WaitToken.IsCancellationRequested, Is.False);
            Assert.That(waiting.IsCompleted, Is.False);
            ITransportWaitingConnection peer = context.Peer();
            context.ReleaseWait.TrySetResult(peer);
            Assert.That(await waiting.WaitAsync(s_bound).ConfigureAwait(false), Is.SameAs(peer));
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task StopCancelsActiveAndQueuedStartsBeforeInstallingAnyRuntime()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            Task first = context.BeginStart(cooperative: true);
            await context.StartEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);
            Task queued = context.Owner.StartAsync(context.Profile);

            Task stopping = context.Owner.StopAsync();

            await Assert.ThatAsync(() => first.WaitAsync(s_bound), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await Assert.ThatAsync(() => queued.WaitAsync(s_bound), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await stopping.WaitAsync(s_bound).ConfigureAwait(false);
            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            context.Factory.Verify(value => value.Create(context.Profile), Times.Once);
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Once);
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task StopRejectsNewWorkUntilNonCooperativeStartupHasDrained()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            Task starting = context.BeginStart(cooperative: false);
            await context.StartEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);
            Task stopping = context.Owner.StopAsync();

            Assert.That(context.StartToken.IsCancellationRequested, Is.True);
            Assert.That(stopping.IsCompleted, Is.False);
            await Assert.ThatAsync(
                () => context.Owner.StartAsync(context.Profile with { ServerUri = "urn:different-server" }),
                Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(() => context.Owner.Acquire(context.Profile), Throws.InvalidOperationException);

            context.ReleaseStart.TrySetResult();
            await Assert.ThatAsync(() => starting.WaitAsync(s_bound), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await stopping.WaitAsync(s_bound).ConfigureAwait(false);
            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            context.Factory.Verify(value => value.Create(It.IsAny<ReverseConnectionProfile>()), Times.Once);
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task APeerReturnedAfterWaitCancellationIsNeverDeliveredAndTheListenerRemainsOwned()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);
            Task<ITransportWaitingConnection> waiting = context.BeginWait(cooperative: false);
            await context.WaitEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);

            await context.Owner.CancelWaitAsync().ConfigureAwait(false);
            context.ReleaseWait.TrySetResult(context.Peer());

            await Assert.ThatAsync(() => waiting.WaitAsync(s_bound), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
            using ReverseConnectionLease lease = context.Owner.Acquire(context.Profile);
            Assert.That(lease.Runtime, Is.SameAs(context.Runtime.Object));
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Never);
        }
    }

    [Test]
    public async Task StopAdmissionPreventsNewLeasesWaitsAndStartsDuringRuntimeShutdown()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);
            Task stopping = context.BeginStop();
            await context.StopEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);

            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopping));
            Assert.That(() => context.Owner.Acquire(context.Profile), Throws.InvalidOperationException);
            await Assert.ThatAsync(() => context.Owner.WaitAsync(context.Profile), Throws.InvalidOperationException)
                .ConfigureAwait(false);
            await Assert.ThatAsync(() => context.Owner.StartAsync(context.Profile), Throws.InvalidOperationException)
                .ConfigureAwait(false);
            context.Runtime.Verify(value => value.WaitAsync(It.IsAny<CancellationToken>()), Times.Never);

            context.ReleaseStop.TrySetResult();
            await stopping.WaitAsync(s_bound).ConfigureAwait(false);
            await context.Owner.StopAsync().ConfigureAwait(false);
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task CanceledRuntimeStopRetainsTheSamePeerPinAndPermitsExplicitStopRetry()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task stopping = context.BeginStop(cancellation.Token);
            await context.StopEntered.Task.WaitAsync(s_bound).ConfigureAwait(false);

            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(() => stopping.WaitAsync(s_bound), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);

            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
            Assert.That(context.Owner.Snapshot.Profile, Is.EqualTo(context.Profile));
            using (ReverseConnectionLease lease = context.Owner.Acquire(context.Profile))
            {
                Assert.That(lease.Runtime, Is.SameAs(context.Runtime.Object));
            }
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Never);
            context.ReleaseStop.TrySetResult();
            await context.Owner.StopAsync().WaitAsync(s_bound).ConfigureAwait(false);
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task ConcurrentDisposalWaitsForTheSessionLeaseAndDisposesTheRuntimeExactlyOnce()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);
            ReverseConnectionLease lease = context.Owner.Acquire(context.Profile);
            Task first;
            Task repeated;
            Task stop;
            try
            {
                first = context.Owner.DisposeAsync().AsTask();
                repeated = context.Owner.DisposeAsync().AsTask();
                stop = context.Owner.StopAsync();
                Assert.That(repeated, Is.SameAs(first));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(stop.IsCompleted, Is.False);
                Assert.That(() => context.Owner.Acquire(context.Profile), Throws.TypeOf<ObjectDisposedException>());
                await Assert.ThatAsync(() => context.Owner.StartAsync(context.Profile),
                    Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
                context.Runtime.Verify(value => value.DisposeAsync(), Times.Never);
            }
            finally
            {
                lease.Dispose();
                lease.Dispose();
            }
            await Task.WhenAll(first, repeated, stop).WaitAsync(s_bound).ConfigureAwait(false);
            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            context.Runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task RuntimeStopFailureStillDisposesItsOwnerAndAllowsExplicitRestart()
    {
        var context = new RuntimeContext();
        await using (context.ConfigureAwait(false))
        {
            var failure = new InvalidOperationException("Controlled stop failure.");
            context.Runtime.Setup(value => value.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromException(failure));
            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);

            await Assert.ThatAsync(() => context.Owner.StopAsync(), Throws.Exception.SameAs(failure))
                .ConfigureAwait(false);

            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            context.Runtime.Verify(value => value.DisposeAsync(), Times.Once);
            var replacement = new Mock<IReverseConnectionRuntime>(MockBehavior.Strict);
            replacement.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            replacement.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            replacement.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            context.Factory.Setup(value => value.Create(context.Profile)).Returns(replacement.Object);

            await context.Owner.StartAsync(context.Profile).ConfigureAwait(false);

            Assert.That(context.Owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Listening));
            using ReverseConnectionLease lease = context.Owner.Acquire(context.Profile);
            Assert.That(lease.Runtime, Is.SameAs(replacement.Object));
        }
    }

    [Test]
    public async Task RuntimeDisposalFailureIsRetainedForRepeatedDisposalAndStop()
    {
        var failure = new InvalidOperationException("Controlled cleanup failure.");
        var runtime = new Mock<IReverseConnectionRuntime>(MockBehavior.Strict);
        runtime.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runtime.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runtime.Setup(value => value.DisposeAsync()).Returns(() => ValueTask.FromException(failure));
        var factory = new Mock<IReverseConnectionRuntimeFactory>(MockBehavior.Strict);
        factory.Setup(value => value.Create(It.IsAny<ReverseConnectionProfile>())).Returns(runtime.Object);
        var owner = new ReverseConnectionService(factory.Object);
        var profile = new ReverseConnectionProfile
        {
            ListenerUrl = "opc.tcp://localhost:4841/client",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory",
            ServerUri = "urn:expected-server"
        };
        await owner.StartAsync(profile).ConfigureAwait(false);

        Task first = owner.DisposeAsync().AsTask();
        Task repeated = owner.DisposeAsync().AsTask();

        Assert.That(repeated, Is.SameAs(first));
        await Assert.ThatAsync(() => first.WaitAsync(s_bound), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        await Assert.ThatAsync(() => repeated, Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        await Assert.ThatAsync(() => owner.StopAsync(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        Assert.That(owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
        runtime.Verify(value => value.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        runtime.Verify(value => value.DisposeAsync(), Times.Once);
    }

    private sealed class RuntimeContext : IAsyncDisposable
    {
        public RuntimeContext()
        {
            Runtime.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Runtime.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Runtime.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            Factory.Setup(value => value.Create(Profile)).Returns(Runtime.Object);
            Owner = new ReverseConnectionService(Factory.Object);
        }

        public ReverseConnectionProfile Profile { get; } = new()
        {
            ListenerUrl = "opc.tcp://localhost:4841/client",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory",
            ServerUri = "urn:expected-server"
        };

        public ReverseConnectionService Owner { get; }

        public Mock<IReverseConnectionRuntime> Runtime { get; } = new(MockBehavior.Strict);

        public Mock<IReverseConnectionRuntimeFactory> Factory { get; } = new(MockBehavior.Strict);

        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WaitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ITransportWaitingConnection> ReleaseWait { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken StartToken { get; private set; }

        public CancellationToken WaitToken { get; private set; }

        public Task BeginStart(bool cooperative)
        {
            Runtime.Setup(value => value.StartAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) =>
            {
                StartToken = ct;
                StartEntered.TrySetResult();
                return cooperative ? ReleaseStart.Task.WaitAsync(ct) : ReleaseStart.Task;
            });
            return Owner.StartAsync(Profile);
        }

        public Task<ITransportWaitingConnection> BeginWait(bool cooperative)
        {
            Runtime.Setup(value => value.WaitAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken ct) =>
            {
                WaitToken = ct;
                WaitEntered.TrySetResult();
                return cooperative ? ReleaseWait.Task.WaitAsync(ct) : ReleaseWait.Task;
            });
            return Owner.WaitAsync(Profile);
        }

        public Task BeginStop(CancellationToken ct = default)
        {
            Runtime.Setup(value => value.StopAsync(It.IsAny<CancellationToken>())).Returns((CancellationToken token) =>
            {
                StopEntered.TrySetResult();
                return ReleaseStop.Task.WaitAsync(token);
            });
            return Owner.StopAsync(ct);
        }

        public ITransportWaitingConnection Peer()
        {
            var peer = new Mock<ITransportWaitingConnection>(MockBehavior.Strict);
            peer.SetupGet(value => value.ServerUri).Returns(Profile.ServerUri);
            peer.SetupGet(value => value.EndpointUrl).Returns(new Uri(Profile.EndpointUrl));
            return peer.Object;
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseStart.TrySetResult();
            ReleaseStop.TrySetResult();
            ReleaseWait.TrySetResult(Peer());
            await Owner.DisposeAsync().AsTask().WaitAsync(s_bound).ConfigureAwait(false);
        }
    }

    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);
}
