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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Capabilities;
using UaLens.Connection;

namespace UaLens.Tests.Capabilities;

[TestFixture]
public sealed class CapabilityServiceTests
{
    [TestCase((int)CapabilityState.Supported, true)]
    [TestCase((int)CapabilityState.Unsupported, false)]
    [TestCase((int)CapabilityState.Unknown, false)]
    [TestCase((int)CapabilityState.RequiresConfiguration, false)]
    [TestCase((int)CapabilityState.Denied, false)]
    public void OnlySupportedEvidenceEnablesAnOperation(int state, bool canExecute)
    {
        var result = new CapabilityResult((CapabilityState)state, "Select an authorized target.");

        Assert.That(result.State, Is.EqualTo((CapabilityState)state));
        Assert.That(result.CanExecute, Is.EqualTo(canExecute));
        Assert.That(result.Reason, Is.EqualTo("Select an authorized target."));
    }

    [TestCaseSource(nameof(s_failedEvidenceCases))]
    public void FailedEvidenceNeverBecomesAnEmptySuccess(StatusCode status, int expected)
    {
        CapabilityResult result = CapabilityResult.FromFailure(status);

        Assert.That(result.State, Is.EqualTo((CapabilityState)expected));
        Assert.That(result.CanExecute, Is.False);
        Assert.That(result.Reason, Is.Not.Empty);
    }

    [Test]
    public void ResultsRejectMissingReasonsAndUndefinedStates()
    {
        Assert.That(() => new CapabilityResult(CapabilityState.Unknown, " "),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => new CapabilityResult((CapabilityState)(-1), "No evidence."),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task ConstructionAndOfflineQueriesNeverProbeTheServer()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            context.Snapshot = context.Snapshot with { Phase = ConnectionPhase.Disconnected };

            CapabilityResult result = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);

            Assert.That(result.State, Is.EqualTo(CapabilityState.RequiresConfiguration));
            Assert.That(result.Reason, Does.Contain("Connect"));
            Assert.That(context.Service.GetCached(s_request), Is.EqualTo(result));
            context.Probe.Verify(
                probe => probe.ProbeAsync(
                    It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task NullTargetRequiresConfigurationWithoutReadingAttributes()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            var request = new CapabilityRequest(NodeId.Null, CapabilityOperation.CallMethod);
            CapabilityResult result = await context.Service.ProbeAsync(request).ConfigureAwait(false);

            Assert.That(result.State, Is.EqualTo(CapabilityState.RequiresConfiguration));
            Assert.That(result.Reason, Does.Contain("Select"));
            context.Probe.Verify(
                probe => probe.ProbeAsync(
                    It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase((int)ConnectionPhase.Connecting)]
    [TestCase((int)ConnectionPhase.Reconnecting)]
    public async Task TemporaryConnectionUnavailabilityIsUnknownRatherThanMissingConfiguration(int phase)
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            context.Snapshot = context.Snapshot with { Phase = (ConnectionPhase)phase };

            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            CapabilityResult result = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            Assert.That(result.State, Is.EqualTo(CapabilityState.Unknown));
            Assert.That(result.Reason, Does.Contain("recovery"));
            context.VerifyProbeCount(0);
        }
    }

    [TestCase((int)CapabilityState.Supported)]
    [TestCase((int)CapabilityState.Unsupported)]
    [TestCase((int)CapabilityState.Denied)]
    public async Task DefinitiveEvidenceIsCachedUntilExpiryOrRefresh(int state)
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            var evidence = new CapabilityResult((CapabilityState)state, "Concrete target evidence.");
            context.Return(evidence);

            CapabilityResult first = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            CapabilityResult second = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);

            Assert.That(second, Is.SameAs(first));
            Assert.That(context.Service.GetCached(s_request), Is.SameAs(evidence));
            context.VerifyProbeCount(1);
            context.Clock.Advance(TimeSpan.FromSeconds(30));
            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.Service.Invalidate();
            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.VerifyProbeCount(3);
        }
    }

    [TestCase((int)CapabilityState.Unknown)]
    [TestCase((int)CapabilityState.RequiresConfiguration)]
    public async Task InconclusiveEvidenceCanBeRetriedImmediately(int state)
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            context.Return(new CapabilityResult((CapabilityState)state, "Configure or retry the target."));
            CapabilityResult first = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.Return(s_supported);
            CapabilityResult second = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);

            Assert.That(first.State, Is.EqualTo((CapabilityState)state));
            Assert.That(second, Is.SameAs(s_supported));
            context.VerifyProbeCount(2);
        }
    }

    [TestCase("timeout")]
    [TestCase("io")]
    [TestCase("service")]
    public async Task TransientFailuresAreUnknownAndNeverNegativelyCached(string failure)
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            Exception error = failure switch
            {
                "timeout" => new TimeoutException("The transport timed out."),
                "io" => new IOException("The transport closed."),
                _ => new ServiceResultException(StatusCodes.BadCommunicationError)
            };
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);

            CapabilityResult first = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.Return(s_supported);
            CapabilityResult second = await context.Service.ProbeAsync(s_request).ConfigureAwait(false);

            Assert.That(first.State, Is.EqualTo(CapabilityState.Unknown));
            Assert.That(first.CanExecute, Is.False);
            Assert.That(second.State, Is.EqualTo(CapabilityState.Supported));
            context.VerifyProbeCount(2);
        }
    }

    [Test]
    public async Task UnexpectedProbeErrorsPropagateInsteadOfBecomingMissingSupport()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            var error = new InvalidOperationException("Fault in a supplied probe.");
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);

            await Assert.ThatAsync(() => context.Service.ProbeAsync(s_request),
                Throws.Exception.SameAs(error)).ConfigureAwait(false);
            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            context.Return(s_supported);
            Assert.That(await context.Service.ProbeAsync(s_request).ConfigureAwait(false), Is.SameAs(s_supported));
        }
    }

    [Test]
    public async Task CallerCancellationIsPropagatedAndTheNextRequestCanSucceed()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISession _, CapabilityRequest _, CancellationToken token) =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    return s_supported;
                });
            using var cancellation = new CancellationTokenSource();
            Task<CapabilityResult> pending = context.Service.ProbeAsync(s_request, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);

            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            context.Return(s_supported);
            Assert.That(await context.Service.ProbeAsync(s_request).ConfigureAwait(false), Is.SameAs(s_supported));
            context.VerifyProbeCount(2);
        }
    }

    [Test]
    public async Task ProbeDeadlineCancelsTheAdapterAndLeavesTheTargetRetryable()
    {
        var context = new ProbeContext(probeTimeout: TimeSpan.FromMilliseconds(100));
        await using (context.ConfigureAwait(false))
        {
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISession _, CapabilityRequest _, CancellationToken token) =>
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                    }
                    // Simulate a peer returning stale evidence after the deadline.
                    return s_supported;
                });

            CapabilityResult timedOut = await context.Service.ProbeAsync(s_request)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(timedOut.State, Is.EqualTo(CapabilityState.Unknown));
            Assert.That(timedOut.Reason, Does.Contain("timed out"));
            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            context.Return(s_supported);
            Assert.That(await context.Service.ProbeAsync(s_request).ConfigureAwait(false), Is.SameAs(s_supported));
            context.VerifyProbeCount(2);
        }
    }

    [Test]
    public async Task PresentationNotificationsWithoutATransitionKeepCurrentEvidence()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.Connection.Raise(connection => connection.StateChanged += null);

            Assert.That(context.Service.GetCached(s_request), Is.SameAs(s_supported));
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.VerifyProbeCount(1);
        }
    }

    [Test]
    public async Task ACancelledCallerCannotCacheEvenALateNegativeResult()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            using var cancellation = new CancellationTokenSource();
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISession _, CapabilityRequest _, CancellationToken _) =>
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    return new CapabilityResult(CapabilityState.Unsupported, "Late negative evidence.");
                });

            await Assert.ThatAsync(() => context.Service.ProbeAsync(s_request, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            context.Return(s_supported);
            Assert.That(await context.Service.ProbeAsync(s_request).ConfigureAwait(false), Is.SameAs(s_supported));
        }
    }

    [TestCase("namespace")]
    [TestCase("session-id")]
    [TestCase("generation")]
    [TestCase("identity")]
    [TestCase("replacement")]
    [TestCase("reconnect")]
    public async Task ServerSessionIdentityAndNamespaceChangesInvalidateEvidence(string change)
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            switch (change)
            {
                case "namespace":
                    context.Namespaces.Update([Namespaces.OpcUa, "urn:replacement"]);
                    break;
                case "session-id":
                    context.Session.SetupGet(session => session.SessionId).Returns(new NodeId("renewed-session", 0));
                    break;
                case "generation":
                    context.Snapshot = context.Snapshot with { Generation = context.Snapshot.Generation + 1 };
                    break;
                case "identity":
                    context.Snapshot = context.Snapshot with
                    {
                        Profile = new ConnectionProfile
                        {
                            EndpointUrl = "opc.tcp://localhost:4840",
                            SecurityMode = MessageSecurityMode.SignAndEncrypt,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                            IdentityType = UserTokenType.UserName,
                            UserTokenPolicyId = "username",
                            IdentityName = "authorized-user"
                        }
                    };
                    break;
                case "replacement":
                    var replacement = new Mock<ISession>();
                    replacement.SetupGet(session => session.SessionId).Returns(new NodeId("test-session", 0));
                    replacement.SetupGet(session => session.NamespaceUris).Returns(context.Namespaces);
                    context.CurrentSession = replacement.Object;
                    break;
                case "reconnect":
                    context.Snapshot = context.Snapshot with { Phase = ConnectionPhase.Reconnecting };
                    context.Connection.Raise(connection => connection.StateChanged += null);
                    context.Snapshot = context.Snapshot with { Phase = ConnectionPhase.Connected };
                    context.Connection.Raise(connection => connection.StateChanged += null);
                    break;
            }

            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.VerifyProbeCount(2);
        }
    }

    [Test]
    public async Task InflightEvidenceCannotRepopulateAnInvalidatedModel()
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISession _, CapabilityRequest _, CancellationToken token) =>
                {
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return s_supported;
                });
            Task<CapabilityResult> pending = context.Service.ProbeAsync(s_request);
            context.Service.Invalidate();
            release.TrySetResult();
            CapabilityResult stale = await pending.ConfigureAwait(false);

            Assert.That(stale.State, Is.EqualTo(CapabilityState.Unknown));
            Assert.That(stale.Reason, Does.Contain("changed"));
            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            context.Return(s_supported);
            Assert.That(await context.Service.ProbeAsync(s_request).ConfigureAwait(false), Is.SameAs(s_supported));
        }
    }

    [Test]
    public async Task CacheCapacityEvictsOldEvidenceWithoutGrowingAnUnboundedTargetList()
    {
        var context = new ProbeContext(capacity: 2);
        await using (context.ConfigureAwait(false))
        {
            var second = new CapabilityRequest(new NodeId("second", 0), CapabilityOperation.ReadValue);
            var third = new CapabilityRequest(new NodeId("third", 0), CapabilityOperation.ReadValue);
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromSeconds(1));
            await context.Service.ProbeAsync(second).ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromSeconds(1));
            await context.Service.ProbeAsync(third).ConfigureAwait(false);

            Assert.That(context.Service.GetCached(s_request).State, Is.EqualTo(CapabilityState.Unknown));
            Assert.That(context.Service.GetCached(second), Is.SameAs(s_supported));
            Assert.That(context.Service.GetCached(third), Is.SameAs(s_supported));
            await context.Service.ProbeAsync(s_request).ConfigureAwait(false);
            context.VerifyProbeCount(4);
        }
    }

    [Test]
    public async Task ConcurrentChecksAreBoundedAndThrottledTargetsRemainRetryable()
    {
        var context = new ProbeContext(maxConcurrentProbes: 1);
        await using (context.ConfigureAwait(false))
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISession _, CapabilityRequest _, CancellationToken token) =>
                {
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return s_supported;
                });
            Task<CapabilityResult> pending = context.Service.ProbeAsync(s_request);
            var second = new CapabilityRequest(new NodeId("second", 0), CapabilityOperation.ReadValue);
            CapabilityResult busy = await context.Service.ProbeAsync(second).ConfigureAwait(false);
            release.TrySetResult();
            await pending.ConfigureAwait(false);

            Assert.That(busy.State, Is.EqualTo(CapabilityState.Unknown));
            Assert.That(busy.Reason, Does.Contain("in progress"));
            context.VerifyProbeCount(1);
            Assert.That(await context.Service.ProbeAsync(second).ConfigureAwait(false), Is.SameAs(s_supported));
            context.VerifyProbeCount(2);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LifecycleReleaseAwaitsCancelledProbeCleanupWithoutOwningTheSession(bool dispose)
    {
        var context = new ProbeContext();
        await using (context.ConfigureAwait(false))
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISession _, CapabilityRequest _, CancellationToken token) =>
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                        return s_supported;
                    }
                    finally
                    {
                        cancelled.TrySetResult();
                        await release.Task.ConfigureAwait(false);
                    }
                });
            Task<CapabilityResult> pending = context.Service.ProbeAsync(s_request);
            context.CurrentSession = null;
            context.Snapshot = context.Snapshot with { Phase = ConnectionPhase.Disconnected };
            Task transition = dispose
                ? context.Service.DisposeAsync().AsTask()
                : context.NotifyConnectionChangedAsync(new CancellationToken(canceled: true));
            Task concurrentTransition = Task.CompletedTask;
            try
            {
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(transition.IsCompleted, Is.False, "The old session must remain alive through cleanup.");
                if (dispose)
                {
                    Assert.That(context.LifecycleObservers, Is.EqualTo(1),
                        "A concurrent disconnect must still await outstanding probe cleanup.");
                    concurrentTransition = context.NotifyConnectionChangedAsync(CancellationToken.None);
                    Assert.That(concurrentTransition.IsCompleted, Is.False);
                    context.Connection.SetupGet(connection => connection.Snapshot)
                        .Throws(new ObjectDisposedException(nameof(IConnectionWorkspace)));
                }
            }
            finally
            {
                release.TrySetResult();
            }
            await transition.ConfigureAwait(false);
            await concurrentTransition.ConfigureAwait(false);
            CapabilityResult result = await pending.ConfigureAwait(false);

            Assert.That(result.State, Is.EqualTo(CapabilityState.Unknown));
            if (dispose)
            {
                Assert.That(() => context.Service.GetCached(s_request), Throws.TypeOf<ObjectDisposedException>());
            }
            else
            {
                Assert.That(context.Service.GetCached(s_request).State,
                    Is.EqualTo(CapabilityState.RequiresConfiguration));
            }
            context.Connection.Verify(connection => connection.DisposeAsync(), Times.Never);
        }
    }

    [Test]
    public async Task DisposalRemovesObserversAndPreventsNewProbes()
    {
        var context = new ProbeContext();
        await context.DisposeAsync().ConfigureAwait(false);
        await context.DisposeAsync().ConfigureAwait(false);

        Assert.That(context.LifecycleObservers, Is.Zero);
        Assert.That(() => context.Service.GetCached(s_request), Throws.TypeOf<ObjectDisposedException>());
        await Assert.ThatAsync(() => context.Service.ProbeAsync(s_request),
            Throws.TypeOf<ObjectDisposedException>()).ConfigureAwait(false);
        context.Connection.VerifyRemove(connection => connection.StateChanged -= It.IsAny<Action>(), Times.Once);
    }

    private sealed class ProbeContext : IAsyncDisposable
    {
        public ProbeContext(int capacity = 128, int maxConcurrentProbes = 4, TimeSpan? probeTimeout = null)
        {
            Session.SetupGet(session => session.NamespaceUris).Returns(Namespaces);
            Session.SetupGet(session => session.SessionId).Returns(new NodeId("test-session", 0));
            CurrentSession = Session.Object;
            Connection.SetupGet(connection => connection.CurrentSession).Returns(() => CurrentSession);
            Connection.SetupGet(connection => connection.IsConnected).Returns(() => Snapshot.IsConnected);
            Connection.SetupGet(connection => connection.Snapshot).Returns(() => Snapshot);
            Connection.SetupAdd(connection =>
                connection.ConnectionChangedAsync += It.IsAny<Func<CancellationToken, Task>>())
                .Callback<Func<CancellationToken, Task>>(handler =>
                {
                    m_connectionChanged += handler;
                    LifecycleObservers++;
                });
            Connection.SetupRemove(connection =>
                connection.ConnectionChangedAsync -= It.IsAny<Func<CancellationToken, Task>>())
                .Callback<Func<CancellationToken, Task>>(handler =>
                {
                    m_connectionChanged -= handler;
                    LifecycleObservers--;
                });
            Return(s_supported);
            Service = new CapabilityService(Connection.Object, Probe.Object, Clock,
                probeTimeout: probeTimeout, capacity: capacity, maxConcurrentProbes: maxConcurrentProbes);
        }

        public Mock<ISession> Session { get; } = new();
        public Mock<IConnectionWorkspace> Connection { get; } = new();
        public Mock<ICapabilityProbe> Probe { get; } = new();
        public NamespaceTable Namespaces { get; } = new([Opc.Ua.Namespaces.OpcUa, "urn:original"]);
        public TestClock Clock { get; } = new();
        public CapabilityService Service { get; }
        public ISession? CurrentSession { get; set; }
        public ConnectionSnapshot Snapshot { get; set; } = new(ConnectionPhase.Connected, null, null, 1);
        public int LifecycleObservers { get; private set; }

        public void Return(CapabilityResult result)
        {
            Probe.Setup(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
        }

        public void VerifyProbeCount(int count)
        {
            Probe.Verify(probe => probe.ProbeAsync(
                It.IsAny<ISession>(), It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(count));
        }

        public Task NotifyConnectionChangedAsync(CancellationToken cancellationToken)
        {
            return m_connectionChanged?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return Service.DisposeAsync();
        }

        private Func<CancellationToken, Task>? m_connectionChanged;
    }

    private sealed class TestClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Interlocked.Read(ref m_ticks);
        }

        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref m_ticks, elapsed.Ticks);
        }

        private long m_ticks;
    }

    private static readonly CapabilityRequest s_request = new(new NodeId("target", 0), CapabilityOperation.ReadValue);
    private static readonly CapabilityResult s_supported = new(CapabilityState.Supported, "Concrete target evidence.");
    private static readonly TestCaseData[] s_failedEvidenceCases =
    [
        new(StatusCodes.BadNodeIdUnknown, (int)CapabilityState.Unsupported),
        new(StatusCodes.BadAttributeIdInvalid, (int)CapabilityState.Unsupported),
        new(StatusCodes.BadServiceUnsupported, (int)CapabilityState.Unsupported),
        new(StatusCodes.BadUserAccessDenied, (int)CapabilityState.Denied),
        new(StatusCodes.BadSecurityModeInsufficient, (int)CapabilityState.Denied),
        new(StatusCodes.BadTimeout, (int)CapabilityState.Unknown),
        new(StatusCodes.BadSessionClosed, (int)CapabilityState.Unknown),
        new(StatusCodes.Good, (int)CapabilityState.Unknown)
    ];
}
