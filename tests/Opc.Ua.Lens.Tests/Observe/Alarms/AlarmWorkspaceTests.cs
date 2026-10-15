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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Alarms;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class AlarmWorkspaceTests
{
    [Test]
    public async Task StartsOneObservationAndRefreshWithoutAnyOperatorMutation()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);

        await context.StartAsync().ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);

        context.Backend.Verify(backend => backend.OpenAsync(
            It.IsAny<AlarmSource>(), TimeSpan.FromMilliseconds(250), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.Observation.RefreshCount, Is.EqualTo(1));
        Assert.That(context.Observation.ExecuteCount, Is.Zero);
        Assert.That(context.Workspace.Snapshot().RefreshState, Is.EqualTo(AlarmRefreshState.Requested));
        Assert.That(context.Workspace.Snapshot().IsObserving, Is.True);
    }

    [Test]
    public async Task CommandTargetsConditionAndSelectedBranchLatestEventNotSource()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition(1, branch: 42)).ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition(2, branch: 42)).ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition(3)).ConfigureAwait(false);

        AlarmCommandResult result = await context.Workspace.ExecuteAsync(
            AlarmTestData.Condition(branch: 42).Key, AlarmOperationKind.Acknowledge, "Operator checked")
            .ConfigureAwait(false);

        Assert.That(result.Outcome, Is.EqualTo(AlarmCommandOutcome.Accepted));
        Assert.That(context.Observation.LastCommand!.Condition.Key.ConditionId, Is.EqualTo(new NodeId(500u, 2)));
        Assert.That(context.Observation.LastCommand.Condition.Key.BranchId, Is.EqualTo(new NodeId(42u, 2)));
        Assert.That(context.Observation.LastCommand.Condition.EventId, Is.EqualTo(ByteString.From([2])));
        Assert.That(context.Observation.LastCommand.Condition.SourceNode, Is.EqualTo(new NodeId(100u, 2)));
        Assert.That(
            context.Workspace.Snapshot().Conditions.ToList().All(static row => row.Condition.Acknowledged == false),
            Is.True);
        Assert.That(result.Detail, Does.Contain("not changed optimistically"));
    }

    [TestCaseSource(nameof(s_rejectedCommandCases))]
    public async Task RejectedCommandIsVisibleAndDoesNotChangeOrRetryState(StatusCode statusCode, int outcome)
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.Observation.Execute = (_, _) => throw new ServiceResultException(statusCode);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);

        AlarmCommandResult result = await context.Workspace.ExecuteAsync(
            AlarmTestData.Condition().Key, AlarmOperationKind.Confirm, "Reviewed").ConfigureAwait(false);

        Assert.That(result.Outcome, Is.EqualTo((AlarmCommandOutcome)outcome));
        Assert.That(result.Detail, Does.Contain(statusCode.ToString()));
        Assert.That(context.Observation.ExecuteCount, Is.EqualTo(1));
        Assert.That(context.Workspace.Snapshot().Conditions.ToList().Single().Condition.Confirmed, Is.False);
        Assert.That(context.Workspace.LastResult, Is.EqualTo(result));
    }

    [Test]
    public async Task CloseCancelsInFlightCommandAndReleasesObservationExactlyOnce()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.Observation.Execute = async (_, token) =>
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);
        Task<AlarmCommandResult> command = context.Workspace.ExecuteAsync(
            AlarmTestData.Condition().Key, AlarmOperationKind.Acknowledge);
        await context.Observation.ExecuteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        await context.Workspace.DisposeAsync().ConfigureAwait(false);
        AlarmCommandResult result = await command.ConfigureAwait(false);
        await context.Workspace.DisposeAsync().ConfigureAwait(false);

        Assert.That(result.Outcome, Is.EqualTo(AlarmCommandOutcome.Canceled));
        Assert.That(context.Observation.DisposeCount, Is.EqualTo(1));
        Assert.That(context.Observation.ExecuteCount, Is.EqualTo(1));
        Assert.That(context.Workspace.Snapshot().IsObserving, Is.False);
    }

    [Test]
    public async Task ConcurrentMutationsAreRejectedInsteadOfQueued()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.Observation.Execute = async (_, token) =>
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        Task<AlarmCommandResult> first = context.Workspace.ExecuteAsync(
            AlarmTestData.Condition().Key, AlarmOperationKind.Acknowledge, cancellationToken: cancellation.Token);
        await context.Observation.ExecuteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        AlarmCommandResult second = await context.Workspace.ExecuteAsync(
            AlarmTestData.Condition().Key, AlarmOperationKind.Confirm).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        AlarmCommandResult canceled = await first.ConfigureAwait(false);

        Assert.That(second.Outcome, Is.EqualTo(AlarmCommandOutcome.Unavailable));
        Assert.That(second.Detail, Does.Contain("no second command was queued"));
        Assert.That(canceled.Outcome, Is.EqualTo(AlarmCommandOutcome.Canceled));
        Assert.That(context.Observation.ExecuteCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StopCancelsPendingOpenBeforeReturning()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        context.Backend.Setup(backend => backend.OpenAsync(
                It.IsAny<AlarmSource>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(async (AlarmSource _, TimeSpan _, CancellationToken token) =>
            {
                observed = token;
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return context.Observation;
            });
        Task opening = context.StartAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        await context.Workspace.StopAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => opening, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(observed.IsCancellationRequested, Is.True);
        Assert.That(context.Workspace.Snapshot().IsObserving, Is.False);
        Assert.That(context.Observation.ExecuteCount, Is.Zero);
    }

    [Test]
    public async Task RefreshMethodSuccessAloneDoesNotClaimCompleteState()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);

        Assert.That(context.Workspace.Snapshot().RefreshState, Is.EqualTo(AlarmRefreshState.Requested));
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshStart, 11));
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.Condition, 11, AlarmTestData.Condition()));
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshEnd, 11));
        await AlarmTestContext.WaitUntilAsync(
            () => context.Workspace.Snapshot().RefreshState == AlarmRefreshState.Complete).ConfigureAwait(false);

        Assert.That(context.Workspace.Snapshot().Conditions, Has.Count.EqualTo(1));
        Assert.That(context.Workspace.Snapshot().Conditions[0].IsStale, Is.False);
    }

    [Test]
    public async Task FailedRefreshIsIncompleteAndRetainsCapturedConditions()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.Observation.Refresh = _ => throw new ServiceResultException(StatusCodes.BadUserAccessDenied);

        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);

        AlarmSnapshot snapshot = context.Workspace.Snapshot();
        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(snapshot.RefreshDetail, Does.Contain("Denied"));
        Assert.That(snapshot.Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RefreshRequiredDuringRefreshIsCoalescedUntilTheCurrentEnd()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshStart, 11));
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshRequired, 11));
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshEnd, 11));

        await AlarmTestContext.WaitUntilAsync(() => context.Observation.RefreshCount == 2).ConfigureAwait(false);

        Assert.That(context.Workspace.Snapshot().RefreshState, Is.EqualTo(AlarmRefreshState.Requested));
        Assert.That(context.Observation.ExecuteCount, Is.Zero);
    }

    [Test]
    public async Task ReplacementReleasesOldObservationAndRefreshesWithoutReplayingCommands()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);
        var replacement = new FakeAlarmObservation();
        context.Backend.Setup(backend => backend.OpenAsync(
                It.IsAny<AlarmSource>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IAlarmObservation>(replacement));

        await context.Workspace.StartAsync(
            new AlarmSource("i=2253", "Server"), TimeSpan.FromMilliseconds(250), generation: 2).ConfigureAwait(false);

        Assert.That(context.Observation.DisposeCount, Is.EqualTo(1));
        Assert.That(replacement.RefreshCount, Is.EqualTo(1));
        Assert.That(replacement.ExecuteCount, Is.Zero);
        Assert.That(context.Workspace.Snapshot().Conditions.ToList().Single().IsStale, Is.True);

        replacement.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshStart, 11));
        replacement.Publish(new AlarmUpdate(AlarmUpdateKind.Condition, 11, AlarmTestData.Condition(2)));
        replacement.Publish(new AlarmUpdate(AlarmUpdateKind.RefreshEnd, 11));
        await AlarmTestContext.WaitUntilAsync(
            () => context.Workspace.Snapshot().RefreshState == AlarmRefreshState.Complete).ConfigureAwait(false);
        Assert.That(
            context.Workspace.Snapshot().Conditions.ToList().Single().Condition.EventId,
            Is.EqualTo(ByteString.From([2])));
    }

    [Test]
    public async Task RecoveryRequestsRefreshButNeverAcknowledges()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.Unavailable, Detail: "Disconnected"));
        context.Observation.Publish(new AlarmUpdate(AlarmUpdateKind.Recovered, Detail: "Recovered"));

        await AlarmTestContext.WaitUntilAsync(() => context.Observation.RefreshCount == 2).ConfigureAwait(false);

        Assert.That(context.Workspace.Snapshot().Conditions.ToList().Single().IsStale, Is.True);
        Assert.That(context.Workspace.Snapshot().RefreshState, Is.EqualTo(AlarmRefreshState.Requested));
        Assert.That(context.Observation.ExecuteCount, Is.Zero);
    }

    [Test]
    public async Task OutOfBandQueueLossIsVisibleEvenIfNoMoreEventsArrive()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);
        context.Observation.DroppedUpdates = 17;

        AlarmSnapshot snapshot = context.Workspace.Snapshot();
        AlarmCommandResult result = await context.Workspace.ExecuteAsync(
            AlarmTestData.Condition().Key, AlarmOperationKind.Acknowledge).ConfigureAwait(false);

        Assert.That(snapshot.DroppedUpdates, Is.EqualTo(17));
        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(result.Outcome, Is.EqualTo(AlarmCommandOutcome.Stale));
        Assert.That(context.Observation.ExecuteCount, Is.Zero);
    }

    [Test]
    public async Task ExplicitSelectionRevisionCannotSilentlyMutateANewerEvent()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition(1)).ConfigureAwait(false);
        ByteString displayed = context.Workspace.Snapshot().Conditions.ToList().Single().Condition.EventId;
        await context.SendAsync(AlarmTestData.Condition(2)).ConfigureAwait(false);

        AlarmCommandResult result = await context.Workspace.ExecuteAsync(
            AlarmTestData.Condition().Key, AlarmOperationKind.Acknowledge, expectedEventId: displayed)
            .ConfigureAwait(false);

        Assert.That(result.Outcome, Is.EqualTo(AlarmCommandOutcome.Stale));
        Assert.That(result.Detail, Does.Contain("newer event"));
        Assert.That(context.Observation.ExecuteCount, Is.Zero);
    }

    [Test]
    public async Task ResetDropsVolatileStateWithoutStartingASecondObservation()
    {
        var context = new AlarmTestContext();
        await using var lifetime = context.ConfigureAwait(false);
        await context.StartAsync().ConfigureAwait(false);
        await context.SendAsync(AlarmTestData.Condition()).ConfigureAwait(false);

        await context.Workspace.ResetAsync().ConfigureAwait(false);

        AlarmSnapshot snapshot = context.Workspace.Snapshot();
        Assert.That(snapshot.Conditions.IsEmpty, Is.True);
        Assert.That(snapshot.History.IsEmpty, Is.True);
        Assert.That(snapshot.IsObserving, Is.False);
        Assert.That(context.Observation.DisposeCount, Is.EqualTo(1));
        context.Backend.Verify(backend => backend.OpenAsync(
            It.IsAny<AlarmSource>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static readonly TestCaseData[] s_rejectedCommandCases =
    [
        new(StatusCodes.BadUserAccessDenied, (int)AlarmCommandOutcome.Denied),
        new(StatusCodes.BadNotSupported, (int)AlarmCommandOutcome.Unsupported),
        new(StatusCodes.BadMethodInvalid, (int)AlarmCommandOutcome.Unsupported),
        new(StatusCodes.BadEventIdUnknown, (int)AlarmCommandOutcome.Failed)
    ];
}
