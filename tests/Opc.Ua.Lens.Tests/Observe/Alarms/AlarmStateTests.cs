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

using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Alarms;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class AlarmStateTests
{
    [Test]
    public void SeparatesBranchesAndKeepsLatestExactEventId()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition(1));
        Apply(state, AlarmTestData.Condition(2, branch: 42));
        Apply(state, AlarmTestData.Condition(3));

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now);

        Assert.That(snapshot.Conditions, Has.Count.EqualTo(2));
        AlarmRow current = snapshot.Conditions.ToList().Single(static row => row.Condition.Key.BranchId.IsNull);
        AlarmRow branch = snapshot.Conditions.ToList().Single(static row => !row.Condition.Key.BranchId.IsNull);
        Assert.That(current.Condition.EventId, Is.EqualTo(ByteString.From([3])));
        Assert.That(branch.Condition.EventId, Is.EqualTo(ByteString.From([2])));
        Assert.That(branch.Condition.Key.BranchId, Is.EqualTo(new NodeId(42u, 2)));
        Assert.That(current.Condition.SourceNode, Is.Not.EqualTo(current.Condition.Key.ConditionId));
    }

    [Test]
    public void CompleteRefreshPrunesOnlyAfterAllExpectedPartitionsEnd()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition(1));
        Apply(state, AlarmTestData.Condition(2, branch: 42), 22);
        Assert.That(state.RequestRefresh([11, 22], AlarmTestData.Now), Is.True);
        Control(state, AlarmUpdateKind.RefreshStart, 11);
        Control(state, AlarmUpdateKind.RefreshStart, 22);
        Apply(state, AlarmTestData.Condition(1));
        Control(state, AlarmUpdateKind.RefreshEnd, 11);

        Assert.That(state.Snapshot(AlarmTestData.Now).Conditions, Has.Count.EqualTo(2));
        Assert.That(state.Snapshot(AlarmTestData.Now).RefreshState, Is.EqualTo(AlarmRefreshState.Receiving));

        Control(state, AlarmUpdateKind.RefreshEnd, 22);
        AlarmSnapshot complete = state.Snapshot(AlarmTestData.Now);
        Assert.That(complete.RefreshState, Is.EqualTo(AlarmRefreshState.Complete));
        Assert.That(complete.Conditions, Has.Count.EqualTo(1));
        Assert.That(complete.Conditions[0].Condition.Key.BranchId.IsNull, Is.True);
        Assert.That(complete.Conditions[0].IsStale, Is.False);
    }

    [Test]
    public void RefreshWithoutEndIsIncompleteAndPreservesPreviousBranches()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition());
        state.RequestRefresh([11], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart);

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now.AddSeconds(31));

        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(snapshot.RefreshDetail, Does.Contain("30 seconds"));
        Assert.That(snapshot.Conditions, Has.Count.EqualTo(1));
        Assert.That(snapshot.Conditions[0].IsStale, Is.True);
        Assert.That(state.IsRefreshActive, Is.False);
    }

    [Test]
    public void LossDuringRefreshPreventsPruningEvenWhenEndArrives()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition());
        state.RequestRefresh([11], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart);
        state.ObserveDroppedUpdates(3, AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshEnd);

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now);

        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(snapshot.DroppedUpdates, Is.EqualTo(3));
        Assert.That(snapshot.Conditions, Has.Count.EqualTo(1));
        Assert.That(snapshot.Conditions[0].IsStale, Is.True);
    }

    [Test]
    public void ReconnectMarksRowsStaleAndRefreshReconcilesNewServerPartition()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition());
        Apply(state, AlarmTestData.Condition(2, branch: 42));
        state.SetObserving(false, AlarmTestData.Now, "Disconnected");
        Assert.That(state.Snapshot(AlarmTestData.Now).Conditions.ToList().All(static row => row.IsStale), Is.True);
        state.SetObserving(true, AlarmTestData.Now, "Recreated");
        state.RequestRefresh([99], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart, 99);
        Apply(state, AlarmTestData.Condition(3), 99);
        Control(state, AlarmUpdateKind.RefreshEnd, 99);

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now);

        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Complete));
        Assert.That(snapshot.Conditions, Has.Count.EqualTo(1));
        Assert.That(snapshot.Conditions[0].Condition.EventId, Is.EqualTo(ByteString.From([3])));
        Assert.That(snapshot.Conditions[0].PartitionId, Is.EqualTo(99));
    }

    [Test]
    public void OlderRefreshRecordDoesNotOverwriteNewerLiveEventId()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition(7));
        state.RequestRefresh([11], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart);
        Apply(state, AlarmTestData.Condition(2));
        Control(state, AlarmUpdateKind.RefreshEnd);

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now);

        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Complete));
        Assert.That(snapshot.Conditions[0].Condition.EventId, Is.EqualTo(ByteString.From([7])));
        Assert.That(snapshot.Conditions[0].IsStale, Is.False);
    }

    [Test]
    public void LiveEventIdRemainsLatestWhenServerClockMovesBackwards()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition(7));
        Apply(state, AlarmTestData.Condition(8) with { Time = AlarmTestData.Now.UtcDateTime.AddDays(-1) });

        Assert.That(state.Snapshot(AlarmTestData.Now).Conditions.ToList().Single().Condition.EventId,
            Is.EqualTo(ByteString.From([8])));
    }

    [Test]
    public void RefreshOnNewGenerationDoesNotCompareAgainstOldServerClock()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition(7));
        state.SetObserving(false, AlarmTestData.Now, "Disconnected");
        state.SetObserving(true, AlarmTestData.Now, "New session");
        state.RequestRefresh([11], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart);
        Apply(state, AlarmTestData.Condition(8) with { Time = AlarmTestData.Now.UtcDateTime.AddDays(-1) });
        Control(state, AlarmUpdateKind.RefreshEnd);

        Assert.That(state.Snapshot(AlarmTestData.Now).RefreshState, Is.EqualTo(AlarmRefreshState.Complete));
        Assert.That(state.Snapshot(AlarmTestData.Now).Conditions.ToList().Single().Condition.EventId,
            Is.EqualTo(ByteString.From([8])));
    }

    [Test]
    public void DuplicateRefreshStartCannotClaimACompleteSnapshot()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition());
        state.RequestRefresh([11], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart);
        Control(state, AlarmUpdateKind.RefreshStart);
        Control(state, AlarmUpdateKind.RefreshEnd);

        Assert.That(state.Snapshot(AlarmTestData.Now).RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(state.Snapshot(AlarmTestData.Now).Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void RefreshRequiredInvalidatesPreviouslyCompleteState()
    {
        AlarmState state = Observing();
        state.RequestRefresh([11], AlarmTestData.Now);
        Control(state, AlarmUpdateKind.RefreshStart);
        Apply(state, AlarmTestData.Condition());
        Control(state, AlarmUpdateKind.RefreshEnd);
        Control(state, AlarmUpdateKind.RefreshRequired);

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now);
        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(snapshot.RefreshDetail, Does.Contain("RefreshRequired"));
        Assert.That(snapshot.Conditions[0].IsStale, Is.True);
    }

    [Test]
    public void StateHistoryAndEvictionEvidenceAreBounded()
    {
        var state = new AlarmState(conditionCapacity: 2, historyCapacity: 3);
        state.SetObserving(true, AlarmTestData.Now, "Started");
        for (uint id = 1; id <= 20; id++)
        {
            Apply(state, AlarmTestData.Condition(condition: id));
        }

        AlarmSnapshot snapshot = state.Snapshot(AlarmTestData.Now);

        Assert.That(snapshot.Conditions, Has.Count.EqualTo(2));
        Assert.That(snapshot.History, Has.Count.LessThanOrEqualTo(3));
        Assert.That(snapshot.EvictedConditions, Is.EqualTo(18));
        Assert.That(snapshot.RefreshState, Is.EqualTo(AlarmRefreshState.Incomplete));
        Assert.That(snapshot.Conditions.ToList().Select(static row => row.Condition.Key.ConditionId),
            Is.EquivalentTo(new[] { new NodeId(19u, 2), new NodeId(20u, 2) }));
    }

    [Test]
    public void RetainFalseIsKeptAsTheLatestStateAndNotAsAnOldRetainedBranch()
    {
        AlarmState state = Observing();
        Apply(state, AlarmTestData.Condition());
        Apply(state, AlarmTestData.Condition(2, retain: false));

        AlarmRow row = state.Snapshot(AlarmTestData.Now).Conditions.ToList().Single();
        Assert.That(row.Condition.Retain, Is.False);
        Assert.That(row.Condition.EventId, Is.EqualTo(ByteString.From([2])));
    }

    [Test]
    public void EvictionPrefersNonRetainedRowsOverAnOlderRetainedAlarm()
    {
        var state = new AlarmState(conditionCapacity: 2);
        state.SetObserving(true, AlarmTestData.Now, "Started");
        Apply(state, AlarmTestData.Condition(condition: 1));
        Apply(state, AlarmTestData.Condition(condition: 2, retain: false));
        Apply(state, AlarmTestData.Condition(condition: 3, retain: false));

        Assert.That(state.Snapshot(AlarmTestData.Now).Conditions.ToList()
            .Select(static row => row.Condition.Key.ConditionId),
            Is.EquivalentTo(new[] { new NodeId(1u, 2), new NodeId(3u, 2) }));
    }

    [Test]
    public void InFlightRefreshRequestsAreCoalesced()
    {
        AlarmState state = Observing();
        Assert.That(state.RequestRefresh([11], AlarmTestData.Now), Is.True);
        Assert.That(state.RequestRefresh([11], AlarmTestData.Now), Is.False);
        Assert.That(state.IsRefreshActive, Is.True);
    }

    private static AlarmState Observing()
    {
        var state = new AlarmState();
        state.SetObserving(true, AlarmTestData.Now, "Test source ready");
        return state;
    }

    private static void Apply(AlarmState state, AlarmCondition condition, uint partition = 11)
    {
        state.Apply(new AlarmUpdate(AlarmUpdateKind.Condition, partition, condition), AlarmTestData.Now);
    }

    private static void Control(AlarmState state, AlarmUpdateKind kind, uint partition = 11)
    {
        state.Apply(new AlarmUpdate(kind, partition), AlarmTestData.Now);
    }
}
